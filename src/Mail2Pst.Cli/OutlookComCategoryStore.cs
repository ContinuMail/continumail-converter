// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Cli;

/// <summary>
/// IOutlookCategoryStore backed by Outlook via LATE-BOUND COM (no Microsoft.Office.Interop reference). MUST
/// be constructed and used on an STA thread (see ImportColoursCommand).
///
/// It does NOT use the Outlook Object Model's <c>Categories.Add</c> — that commits the master category list
/// lazily and racily, persisting only a nondeterministic subset of a batch (verified across ~10 experiments:
/// 1/7, 2/3, 3/7, 4/7 survivors with every teardown variant). Instead it edits the master list's backing
/// XML directly: the Calendar folder's <c>IPM.Configuration.CategoryList</c> associated (FAI) message carries
/// the whole list as a UTF-8 <c>PidTagRoamingXmlStream</c>. We read that XML (<see cref="CategoryListXml"/>),
/// append one node per buffered Add, and write it back in a single <c>StorageItem.Save</c> — one atomic
/// commit, no per-add race (verified deterministic: 4/4 runs persisted 7/7).
///
/// Requires Outlook to be CLOSED: we start a transient instance, never touch its in-memory Categories cache
/// (so it cannot re-serialize a stale list over our write), and on Dispose call Quit so the store flushes to
/// disk (a hard kill loses the Save). A user's already-running Outlook caches the list and could overwrite or
/// not see our change until restart, so we refuse to run against it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OutlookComCategoryStore : IOutlookCategoryStore, IDisposable
{
    private const string RoamingXmlStreamProp = "http://schemas.microsoft.com/mapi/proptag/0x7C080102";
    private const int OlFolderCalendar = 9;
    private const int ShutdownWaitMs = 45_000;       // Dispose clean-exit cap: wait for a clean exit when we wrote a FAI Save (flush window)
    private const int NoChangeShutdownWaitMs = 3_000; // nothing saved -> nothing to flush, so kill the linger fast

    private readonly object _app;
    private readonly object _session;
    private readonly dynamic _storage;     // the IPM.Configuration.CategoryList FAI StorageItem
    private readonly string _originalXml;
    private readonly IReadOnlySet<string> _existing;
    private readonly List<(string Name, int OutlookColor)> _pending = new();
    private readonly int[] _startedPids;   // OUTLOOK.EXE PIDs that appeared when we created the instance
    private readonly bool _readOnly;
    private bool _savedChanges;            // true once Commit() actually wrote+saved the FAI (picks the wait budget)
    private bool _shutdownCalled;          // Shutdown() is idempotent; Dispose calls it at most once

    /// <summary>The transient OUTLOOK.EXE PIDs this instance started. Empty when a TOCTOU race meant we
    /// attached to a pre-existing (the user's) Outlook — in that case we never touch it (KB-004), so there
    /// is nothing to wait on.</summary>
    internal IReadOnlyList<int> StartedPids => _startedPids;

    /// <summary>True once <see cref="Shutdown"/> has run and every PID in <see cref="StartedPids"/> exited
    /// on its own within the wait budget, without needing a forced <c>Kill</c>. False if nothing was ever
    /// started (TOCTOU race — see <see cref="StartedPids"/>), if <see cref="Shutdown"/> hasn't run yet, or
    /// if any started PID had to be killed.</summary>
    internal bool CleanExit { get; private set; }

    internal OutlookComCategoryStore(string profileName, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName);
        _readOnly = readOnly;

        int[] before = OutlookPids();
        if (before.Length != 0)
            throw new InvalidOperationException(
                "Outlook is running. Close Outlook completely, then re-run import-colours --apply.");

        Type? t = Type.GetTypeFromProgID("Outlook.Application");
        if (t is null) throw new InvalidOperationException("Outlook is not installed (ProgID not registered).");
        _app = Activator.CreateInstance(t) ?? throw new InvalidOperationException("Could not start Outlook.");
        _startedPids = OutlookPids().Except(before).ToArray(); // the transient instance(s) we just started

        dynamic app = _app;
        dynamic session = app.GetNamespace("MAPI");
        // Explicit-name logon: MAPI's DEFAULT-profile resolution (Logon(null)) can be broken
        // machine-wide while by-name logon works (owner machine, 2026-07-05). Callers resolve
        // the name via OutlookProfileResolver first.
        session.Logon(profileName, null, false, false);
        _session = session;

        _storage = OpenCategoryListStorage(session, bootstrap: !readOnly);
        try { _originalXml = Encoding.UTF8.GetString(ReadBytes(_storage)); }
        catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8004010F)
        { _originalXml = string.Empty; }
        _existing = CategoryListXml.ReadNames(_originalXml);
    }

    public IReadOnlySet<string> ExistingNames() => _existing;

    public void Add(string name, int outlookColorIndex) => _pending.Add((name, outlookColorIndex));

    public void Commit()
    {
        if (_readOnly) throw new InvalidOperationException("Cannot Commit on a read-only store.");
        SaveCore();
    }

    /// <summary>Persists the buffered adds (as <see cref="Commit()"/>), then — if <paramref name="expectedAdded"/>
    /// is non-empty — re-reads the FAI fresh (<see cref="ReadPersistedColours"/>) and verifies every expected
    /// name/colour landed (<see cref="CategoryVerify.Missing"/>). If something is missing, retries with a single
    /// re-Save (in-session read-back retry count = 1); if still missing after that, throws
    /// <see cref="ColourReadbackException"/> rather than silently reporting success. An empty
    /// <paramref name="expectedAdded"/> skips the read-back entirely.</summary>
    internal void Commit(IReadOnlyDictionary<string, int> expectedAdded)
    {
        ArgumentNullException.ThrowIfNull(expectedAdded);
        if (_readOnly) throw new InvalidOperationException("Cannot Commit on a read-only store.");
        SaveCore();
        if (expectedAdded.Count == 0) return;

        IReadOnlyList<string> missing = CategoryVerify.Missing(expectedAdded, ReadPersistedColours());
        if (missing.Count == 0) return;

        // In-session read-back retry: re-Save once (the XML already staged in the FAI PropertyAccessor
        // from SaveCore() above) in case the first Save didn't flush/commit visibly to a fresh re-read.
        _storage.Save();
        missing = CategoryVerify.Missing(expectedAdded, ReadPersistedColours());
        if (missing.Count > 0) throw new ColourReadbackException();
    }

    private void SaveCore()
    {
        if (_pending.Count == 0) return;
        string newXml = CategoryListXml.Append(_originalXml, _pending);
        dynamic pa = _storage.PropertyAccessor;
        pa.SetProperty(RoamingXmlStreamProp, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(newXml));
        _storage.Save(); // single atomic commit of the FAI message
        _savedChanges = true;
        _pending.Clear();
    }

    /// <summary>Fresh re-read of the persisted master category list: a brand-new <c>GetStorage</c> call (not
    /// the in-memory <c>_storage</c>/<c>_originalXml</c> we wrote from) followed by a decode of the
    /// name-to-colour mapping. Used by <see cref="Commit(IReadOnlyDictionary{string,int})"/>'s read-back
    /// verification and available for read-only verifier use.</summary>
    internal IReadOnlyDictionary<string, int> ReadPersistedColours()
    {
        dynamic fresh = OpenCategoryListStorage(_session, bootstrap: false);
        try
        {
            string xml = Encoding.UTF8.GetString(ReadBytes(fresh));
            return CategoryListXml.ReadNameToColourIndex(xml);
        }
        finally { try { Marshal.FinalReleaseComObject(fresh); } catch { /* best-effort */ } }
    }

    // Opens the CategoryList FAI by its identity. olIdentifyByMessageClass (1) is the documented value but
    // errors on this Outlook build; by-subject (2) reliably returns the existing item (its subject equals its
    // message class). Try message-class first for forward-compat, then subject; reject an item that carries no
    // binary XML stream (some identifier types fabricate an empty StorageItem instead of failing).
    //
    // bootstrap: true allows a virgin store (no FAI yet) to bootstrap — GetStorage fabricates an empty
    // StorageItem on HRESULT 0x8004010F and we hand it back so the caller starts a fresh list via
    // CategoryListXml and Save() creates the FAI. bootstrap: false (read-only callers, e.g. the verifier)
    // must NEVER create/mutate the FAI: the same HRESULT instead throws CategoryListMissingException.
    private static dynamic OpenCategoryListStorage(dynamic session, bool bootstrap)
    {
        dynamic calendar = session.GetDefaultFolder(OlFolderCalendar);
        Exception? last = null;
        foreach (int idType in new[] { 1, 2 })
        {
            dynamic candidate;
            try { candidate = calendar.GetStorage("IPM.Configuration.CategoryList", idType); }
            catch (Exception ex) { last = ex; continue; } // identifier not supported on this build
            try { _ = ReadBytes(candidate); return candidate; }
            catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8004010F)
            {
                // Virgin store: the profile's master list was never serialized, so the FAI has no
                // RoamingXmlStream. GetStorage fabricated the item; "" starts a fresh list via
                // CategoryListXml and Save() creates the FAI. ONLY this HRESULT bootstraps —
                // anything else (parse, access, provider) fails loud.
                if (bootstrap) return candidate;
                throw new CategoryListMissingException();
            }
            catch (Exception ex) { last = ex; }            // no usable XML stream — try the next identifier
        }
        throw new InvalidOperationException(
            "Could not open the Outlook master category list (CategoryList FAI).", last);
    }

    // Reads the binary RoamingXmlStream as bytes via the pure, unit-tested normalizer (handles byte[], an
    // integral Array marshaling, and null/DBNull).
    private static byte[] ReadBytes(dynamic storage) =>
        CategoryStreamBytes.FromVariant((object)storage.PropertyAccessor.GetProperty(RoamingXmlStreamProp));

    private static int[] OutlookPids() =>
        Process.GetProcessesByName("OUTLOOK").Select(p => { int id = p.Id; p.Dispose(); return id; }).ToArray();

    /// <summary>Logoff/Quit/wait-for-exit/kill-as-last-resort sequence, extracted from <see cref="Dispose"/>
    /// so callers can shut Outlook down explicitly and observe <see cref="CleanExit"/> before disposing RCWs.
    /// Idempotent — a second call is a no-op. Only ever touches PIDs in <see cref="StartedPids"/> (KB-004):
    /// if that list is empty (a TOCTOU race attached us to the user's already-running Outlook) this does
    /// nothing, and <see cref="CleanExit"/> stays false since nothing of ours ran.</summary>
    internal void Shutdown()
    {
        if (_shutdownCalled) return;
        _shutdownCalled = true;

        // Only shut Outlook down if we actually started it. _startedPids is empty only if a TOCTOU race had
        // CreateInstance attach to a pre-existing OUTLOOK.EXE that appeared after the "is Outlook running?"
        // guard — in that case it is the user's process, so we must NOT Quit it.
        if (_startedPids.Length == 0) return;

        // Logoff then Quit (clean shutdown) flushes our Save to disk; a hard kill would lose it. We never
        // dirtied the OOM Categories cache, so Outlook won't re-serialize a stale list over the write.
        try { ((dynamic)_session).Logoff(); } catch { /* best-effort */ }
        try { ((dynamic)_app).Quit(); } catch { /* best-effort */ }

        // Wait for the OUTLOOK.EXE we started to exit — that is the flush-complete signal — bounded so a
        // hung instance can't block the CLI. If Quit() does NOT terminate it within the budget (seen on a
        // no-op run: nothing was Saved, so Outlook has no MAPI change to flush and lingers), force-kill it.
        // CRITICAL (KB-004): the COM-launched OUTLOOK.EXE inherits THIS process's stdout pipe
        // handle. A lingering instance keeps that pipe open after the CLI exits, so the GUI's
        // `.output()` (which reads stdout to EOF) blocks until Outlook finally dies — effectively forever
        // → the colour-import spinner hangs. Killing the instance we started releases the pipe at once.
        // We only kill PIDs in _startedPids (our own transient instance), never the user's Outlook. By this
        // point Commit()'s Save (if any) has had the full wait budget to flush a tiny FAI XML write.
        // Full flush window only when we actually wrote a Save; otherwise there is nothing to flush, so a
        // lingering instance gets killed after a short grace (the common re-import-existing case → fast).
        int waitBudget = _savedChanges ? ShutdownWaitMs : NoChangeShutdownWaitMs;
        var sw = Stopwatch.StartNew();
        bool clean = true;
        foreach (int pid in _startedPids)
        {
            Process? p = null;
            try
            {
                p = Process.GetProcessById(pid);
                int remaining = waitBudget - (int)sw.ElapsedMilliseconds;
                if (remaining > 0) p.WaitForExit(remaining);
                if (!p.HasExited)
                {
                    // A PID that hasn't self-exited within budget breaks the clean-exit guarantee the moment
                    // we observe it — set the verdict BEFORE attempting Kill(), so an access-denied
                    // Win32Exception (or a race where the process dies between this check and Kill) can't be
                    // swallowed by the catch and leave clean == true.
                    clean = false;
                    p.Kill(entireProcessTree: true); // Quit() didn't take — don't leak/hold the pipe

                    // Best-effort bounded wait for the kill to actually land before Shutdown() (and thus
                    // Dispose()) returns. Kill() is asynchronous — without this, a retry's next attempt can
                    // run OutlookPids() while this instance is still terminating and see it, throwing the
                    // non-transient "Outlook is running" error and defeating the retry (mirrors the post-kill
                    // WaitForExit(5000) in ProcessShutdown.WaitForCleanExit, IProcessHandle.cs). Best-effort:
                    // clean is already false, so a failed/timed-out wait doesn't change the verdict.
                    try { p.WaitForExit(5000); } catch { /* best-effort */ }
                }
            }
            catch { /* already exited / not found / access denied */ }
            finally { p?.Dispose(); }
        }
        CleanExit = clean;
    }

    public void Dispose()
    {
        Shutdown();

        // FinalReleaseComObject drops our RCWs; the instance has already exited above, so no extra GC pass is
        // needed to tear it down.
        try { Marshal.FinalReleaseComObject(_storage); } catch { /* best-effort */ }
        try { Marshal.FinalReleaseComObject(_session); } catch { }
        try { Marshal.FinalReleaseComObject(_app); } catch { }
    }
}

/// <summary>Thrown by <see cref="OutlookComCategoryStore.Commit(IReadOnlyDictionary{string,int})"/> when a
/// fresh read-back of the persisted FAI still doesn't reflect every expected added category/colour after the
/// single in-session re-Save retry. Surfaced by the CLI as stage <c>colour-apply-readback-failed</c>.</summary>
internal sealed class ColourReadbackException : Exception
{
    internal ColourReadbackException() { }
    internal ColourReadbackException(string message) : base(message) { }
    internal ColourReadbackException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown by a read-only <see cref="OutlookComCategoryStore"/> open (or a fresh
/// <see cref="OutlookComCategoryStore.ReadPersistedColours"/> re-read) when the profile's master category
/// list FAI (<c>IPM.Configuration.CategoryList</c>) doesn't exist yet. Read-only callers must never
/// bootstrap/create it — that mutation is reserved for the write path.</summary>
internal sealed class CategoryListMissingException : Exception
{
    internal CategoryListMissingException() { }
    internal CategoryListMissingException(string message) : base(message) { }
    internal CategoryListMissingException(string message, Exception innerException) : base(message, innerException) { }
}

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
    private const int ShutdownWaitMs = 30_000;

    private readonly object _app;
    private readonly object _session;
    private readonly dynamic _storage;     // the IPM.Configuration.CategoryList FAI StorageItem
    private readonly string _originalXml;
    private readonly IReadOnlySet<string> _existing;
    private readonly List<(string Name, int OutlookColor)> _pending = new();
    private readonly int[] _startedPids;   // OUTLOOK.EXE PIDs that appeared when we created the instance

    internal OutlookComCategoryStore()
    {
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
        session.Logon(null, null, false, false); // ShowDialog=false, NewSession=false: silent default-profile session
        _session = session;

        _storage = OpenCategoryListStorage(session);
        _originalXml = Encoding.UTF8.GetString(ReadBytes(_storage));
        _existing = CategoryListXml.ReadNames(_originalXml);
    }

    public IReadOnlySet<string> ExistingNames() => _existing;

    public void Add(string name, int outlookColorIndex) => _pending.Add((name, outlookColorIndex));

    public void Commit()
    {
        if (_pending.Count == 0) return;
        string newXml = CategoryListXml.Append(_originalXml, _pending);
        dynamic pa = _storage.PropertyAccessor;
        pa.SetProperty(RoamingXmlStreamProp, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(newXml));
        _storage.Save(); // single atomic commit of the FAI message
        _pending.Clear();
    }

    // Opens the CategoryList FAI by its identity. olIdentifyByMessageClass (1) is the documented value but
    // errors on this Outlook build; by-subject (2) reliably returns the existing item (its subject equals its
    // message class). Try message-class first for forward-compat, then subject; reject an item that carries no
    // binary XML stream (some identifier types fabricate an empty StorageItem instead of failing).
    private static dynamic OpenCategoryListStorage(dynamic session)
    {
        dynamic calendar = session.GetDefaultFolder(OlFolderCalendar);
        Exception? last = null;
        foreach (int idType in new[] { 1, 2 })
        {
            dynamic candidate;
            try { candidate = calendar.GetStorage("IPM.Configuration.CategoryList", idType); }
            catch (Exception ex) { last = ex; continue; } // identifier not supported on this build
            try { _ = ReadBytes(candidate); return candidate; }
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

    public void Dispose()
    {
        // Only shut Outlook down if we actually started it. _startedPids is empty only if a TOCTOU race had
        // CreateInstance attach to a pre-existing OUTLOOK.EXE that appeared after the "is Outlook running?"
        // guard — in that case it is the user's process, so we must NOT Quit it.
        if (_startedPids.Length > 0)
        {
            // Logoff then Quit (clean shutdown) flushes our Save to disk; a hard kill would lose it. We never
            // dirtied the OOM Categories cache, so Outlook won't re-serialize a stale list over the write.
            try { ((dynamic)_session).Logoff(); } catch { /* best-effort */ }
            try { ((dynamic)_app).Quit(); } catch { /* best-effort */ }

            // Wait for the OUTLOOK.EXE we started to exit — that is the flush-complete signal — bounded so a
            // hung instance can't block the CLI. (If Logon blocks earlier and the STA Join times out, the
            // store is abandoned un-Disposed and the started instance can leak; that path emits a clear
            // "dismiss any Outlook prompt" error and is rare given ShowDialog=false + Outlook-closed.)
            var sw = Stopwatch.StartNew();
            foreach (int pid in _startedPids)
            {
                Process? p = null;
                try
                {
                    p = Process.GetProcessById(pid);
                    int remaining = ShutdownWaitMs - (int)sw.ElapsedMilliseconds;
                    if (remaining > 0) p.WaitForExit(remaining);
                }
                catch { /* already exited / not found */ }
                finally { p?.Dispose(); }
            }
        }

        // FinalReleaseComObject drops our RCWs; the instance has already exited above, so no extra GC pass is
        // needed to tear it down.
        try { Marshal.FinalReleaseComObject(_storage); } catch { /* best-effort */ }
        try { Marshal.FinalReleaseComObject(_session); } catch { }
        try { Marshal.FinalReleaseComObject(_app); } catch { }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Xml;
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
/// the whole list as a UTF-8 <c>PidTagRoamingXmlStream</c> (MS-OXOCFG §2.2.5.1.1). We read that XML, append
/// one &lt;category&gt; node per buffered Add, and write it back in a single <c>StorageItem.Save</c> — one
/// atomic commit, no per-add race (verified deterministic: 4/4 runs persisted 7/7).
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
    private readonly XmlDocument _doc;
    private readonly XmlElement _root;
    private readonly string _nsUri;
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Name, int OutlookColor)> _pending = new();

    internal OutlookComCategoryStore()
    {
        if (Process.GetProcessesByName("OUTLOOK").Length != 0)
            throw new InvalidOperationException(
                "Outlook is running. Close Outlook completely, then re-run import-colours --apply.");

        Type? t = Type.GetTypeFromProgID("Outlook.Application");
        if (t is null) throw new InvalidOperationException("Outlook is not installed (ProgID not registered).");
        _app = Activator.CreateInstance(t) ?? throw new InvalidOperationException("Could not start Outlook.");
        dynamic app = _app;
        dynamic session = app.GetNamespace("MAPI");
        session.Logon(null, null, false, false); // ShowDialog=false, NewSession=false: silent default-profile session
        _session = session;

        _storage = OpenCategoryListStorage(session);
        _doc = new XmlDocument { PreserveWhitespace = true };
        _doc.LoadXml(ReadXml(_storage));
        _root = _doc.DocumentElement ?? throw new InvalidOperationException("CategoryList XML has no root element.");
        _nsUri = _root.NamespaceURI;
        foreach (XmlNode node in _root.ChildNodes)
            if (node is XmlElement el && el.LocalName == "category")
            {
                string name = el.GetAttribute("name");
                if (!string.IsNullOrEmpty(name)) _existing.Add(name);
            }
    }

    public IReadOnlySet<string> ExistingNames() => _existing;

    public void Add(string name, int outlookColorIndex)
    {
        _pending.Add((name, outlookColorIndex));
        _existing.Add(name);
    }

    public void Commit()
    {
        if (_pending.Count == 0) return;

        foreach ((string name, int outlookColor) in _pending)
        {
            XmlElement cat = _doc.CreateElement("category", _nsUri);
            cat.SetAttribute("name", name);
            // The XML 'color' is the 0-based MS-OXOCFG index; OlCategoryColor (1-25) is that index + 1.
            cat.SetAttribute("color", (outlookColor - 1).ToString(CultureInfo.InvariantCulture));
            cat.SetAttribute("keyboardShortcut", "0");
            cat.SetAttribute("usageCount", "0");
            cat.SetAttribute("guid", "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}");
            _root.AppendChild(cat);
        }
        _root.SetAttribute("lastSavedTime",
            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));

        dynamic pa = _storage.PropertyAccessor;
        pa.SetProperty(RoamingXmlStreamProp, Serialize(_doc));
        _storage.Save(); // single atomic commit of the FAI message
        _pending.Clear();
    }

    // Opens the CategoryList FAI by its identity. olIdentifyByMessageClass (1) is the documented value but
    // errors on this Outlook build; by-subject (2) reliably returns the existing item (its subject equals its
    // message class). Try message-class first for forward-compat, then subject; reject an item that carries no
    // XML stream (some identifier types fabricate an empty StorageItem instead of failing).
    private static dynamic OpenCategoryListStorage(dynamic session)
    {
        dynamic calendar = session.GetDefaultFolder(OlFolderCalendar);
        foreach (int idType in new[] { 1, 2 })
        {
            dynamic? candidate = null;
            try { candidate = calendar.GetStorage("IPM.Configuration.CategoryList", idType); }
            catch { continue; } // identifier not supported on this build
            try { _ = candidate!.PropertyAccessor.GetProperty(RoamingXmlStreamProp); return candidate; }
            catch { /* no XML stream on this item — try the next identifier */ }
        }
        throw new InvalidOperationException("Could not open the Outlook master category list (CategoryList FAI).");
    }

    private static string ReadXml(dynamic storage) =>
        Encoding.UTF8.GetString((byte[])storage.PropertyAccessor.GetProperty(RoamingXmlStreamProp));

    private static byte[] Serialize(XmlDocument doc)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };
        using var ms = new MemoryStream();
        using (XmlWriter w = XmlWriter.Create(ms, settings)) doc.Save(w);
        return ms.ToArray();
    }

    public void Dispose()
    {
        // Quit (clean shutdown) flushes our Save to disk; a hard kill would lose it. We never dirtied the OOM
        // Categories cache, so Outlook won't re-serialize a stale list over the write.
        try { ((dynamic)_app).Quit(); } catch { /* best-effort */ }

        // Wait for the transient OUTLOOK.EXE to exit — that is the flush-complete signal — bounded so a hung
        // instance can't block the CLI. We required Outlook closed at startup, so any instance now is ours.
        var sw = Stopwatch.StartNew();
        foreach (Process p in Process.GetProcessesByName("OUTLOOK"))
        {
            try
            {
                int remaining = ShutdownWaitMs - (int)sw.ElapsedMilliseconds;
                if (remaining > 0) p.WaitForExit(remaining);
            }
            catch { /* already exited / access denied */ }
            finally { p.Dispose(); }
        }

        try { Marshal.FinalReleaseComObject(_storage); } catch { /* best-effort */ }
        try { Marshal.FinalReleaseComObject(_session); } catch { }
        try { Marshal.FinalReleaseComObject(_app); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

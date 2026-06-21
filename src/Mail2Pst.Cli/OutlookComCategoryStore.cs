// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Cli;

/// <summary>
/// IOutlookCategoryStore backed by Outlook via LATE-BOUND COM (no Microsoft.Office.Interop reference). MUST
/// be constructed and used on an STA thread (see ImportColoursCommand). Disposes the COM objects to avoid a
/// ghosted OUTLOOK.EXE. Throws InvalidOperationException if Outlook is unavailable.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class OutlookComCategoryStore : IOutlookCategoryStore, IDisposable
{
    private readonly object _app;
    private readonly object _session;
    private readonly dynamic _categories;

    internal OutlookComCategoryStore()
    {
        Type? t = Type.GetTypeFromProgID("Outlook.Application");
        if (t is null) throw new InvalidOperationException("Outlook is not installed (ProgID not registered).");
        _app = Activator.CreateInstance(t)
               ?? throw new InvalidOperationException("Could not start Outlook.");
        dynamic app = _app;
        _session = app.GetNamespace("MAPI");
        _categories = ((dynamic)_session).Categories;
    }

    public IReadOnlySet<string> ExistingNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic c in _categories) names.Add((string)c.Name);
        return names;
    }

    public void Add(string name, int outlookColorIndex) =>
        _categories.Add(name, outlookColorIndex, 0); // OlCategoryShortcutKey.olCategoryShortcutKeyNone = 0

    public void Dispose()
    {
        try { Marshal.ReleaseComObject(_categories); } catch { /* best-effort */ }
        try { Marshal.ReleaseComObject(_session); } catch { }
        try { Marshal.ReleaseComObject(_app); } catch { }
    }
}

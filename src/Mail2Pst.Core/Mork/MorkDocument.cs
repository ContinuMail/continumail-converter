// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Mail2Pst.Core.Mork;

public sealed class MorkDocument
{
    public IReadOnlyList<MorkTable> Tables { get; }

    public MorkDocument(IEnumerable<MorkTable> tables)
    {
        Tables = new List<MorkTable>(tables).AsReadOnly();
    }

    public IReadOnlyList<MorkTable> GetTables(string scope, string kind) =>
        Tables.Where(t => t.Scope == scope && t.Kind == kind).ToList().AsReadOnly();

    public bool TryGetSingleTable(string scope, string kind, out MorkTable table)
    {
        var m = GetTables(scope, kind);
        table = m.Count == 1 ? m[0] : default!;
        return m.Count == 1;
    }
}

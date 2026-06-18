// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mail2Pst.Core.Mork;

public sealed class MorkTable
{
    public string Id { get; }
    public string? Scope { get; }
    public string? Kind { get; }
    public IReadOnlyDictionary<string, MorkRow> Rows { get; }

    public MorkTable(string id, string? scope, string? kind, IReadOnlyDictionary<string, MorkRow> rows)
    {
        Id = id;
        Scope = scope;
        Kind = kind;
        Rows = new ReadOnlyDictionary<string, MorkRow>(
            new Dictionary<string, MorkRow>(rows, StringComparer.Ordinal));
    }
}

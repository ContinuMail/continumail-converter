// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mail2Pst.Core.Mork;

public sealed class MorkRow
{
    public string Id { get; }
    public IReadOnlyDictionary<string, string> Cells { get; }

    public MorkRow(string id, IReadOnlyDictionary<string, string> cells)
    {
        Id = id;
        Cells = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(cells, StringComparer.Ordinal));
    }

    public bool TryGetCell(string column, out string value) => Cells.TryGetValue(column, out value!);
}

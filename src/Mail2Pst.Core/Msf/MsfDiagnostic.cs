// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.Msf;

/// <summary>A single non-fatal interpretation problem for one cell of one row.</summary>
public sealed class MsfDiagnostic
{
    public string RowId { get; }
    public string Column { get; }
    public string RawValue { get; }
    public string Reason { get; }

    public MsfDiagnostic(string rowId, string column, string rawValue, string reason)
    {
        RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
        Column = column ?? throw new ArgumentNullException(nameof(column));
        RawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.Parsing;

/// <summary>An OPERATIONAL failure of the scan raw-message spill infrastructure (temp file create/write/read —
/// disk full, permission, AV lock, missing temp path). It is deliberately NOT a FormatException/IOException so
/// the per-message skip taxonomy never swallows it as "corrupt mail": it must surface through the fatal path.</summary>
public sealed class RawMessageSpillException : Exception
{
    public RawMessageSpillException(string message, Exception inner) : base(message, inner) { }
}

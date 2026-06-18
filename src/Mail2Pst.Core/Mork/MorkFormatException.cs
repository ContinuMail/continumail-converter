// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.Mork;

/// <summary>Thrown on structural/syntactic Mork corruption. Unknown CONTENT is not an error.</summary>
public sealed class MorkFormatException : Exception
{
    public MorkFormatException(string message) : base(message) { }
    public MorkFormatException(string message, Exception inner) : base(message, inner) { }
}

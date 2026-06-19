// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mail2Pst.Core.Msf;

/// <summary>Result of interpreting a msgs table: typed messages plus any non-fatal diagnostics.</summary>
public sealed class MsfReadResult
{
    public IReadOnlyList<MsfMessage> Messages { get; }
    public IReadOnlyList<MsfDiagnostic> Diagnostics { get; }

    public MsfReadResult(IReadOnlyList<MsfMessage> messages, IReadOnlyList<MsfDiagnostic> diagnostics)
    {
        if (messages is null) throw new ArgumentNullException(nameof(messages));
        if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
        Messages = new ReadOnlyCollection<MsfMessage>(messages.ToList());
        Diagnostics = new ReadOnlyCollection<MsfDiagnostic>(diagnostics.ToList());
    }
}

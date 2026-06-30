// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Pairs an <see cref="AppointmentRecord"/> with the PST folder path it should be written into.
/// Consumed by the calendar write phase (analogous to <see cref="PlannedTask"/>).
/// </summary>
public sealed class PlannedAppointment
{
    public AppointmentRecord Appointment { get; set; } = new();
    public IReadOnlyList<string> TargetFolderPath { get; set; } = Array.Empty<string>();
}

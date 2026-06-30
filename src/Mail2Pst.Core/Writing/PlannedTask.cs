// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Writing;

public class PlannedTask
{
    public TaskRecord Task { get; set; } = new();
    public IReadOnlyList<string> TargetFolderPath { get; set; } = Array.Empty<string>();
}

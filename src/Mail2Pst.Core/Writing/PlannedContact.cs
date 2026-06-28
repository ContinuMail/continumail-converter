// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Writing;

public class PlannedContact
{
    public ContactRecord Contact { get; set; } = new();
    public IReadOnlyList<string> TargetFolderPath { get; set; } = Array.Empty<string>();
}

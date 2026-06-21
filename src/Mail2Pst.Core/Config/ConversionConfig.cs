// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Config;

public class ConversionConfig
{
    public List<OutputGroupConfig> Outputs { get; set; } = new();
    public JunkHandlingMode JunkHandling { get; set; } = JunkHandlingMode.Off;
    public bool DropExpunged { get; set; }
    public string? ProfilePath { get; set; }
}

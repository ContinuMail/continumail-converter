// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Config;

public class SourceConfig
{
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TargetFolder { get; set; }
    public List<string>? TargetFolderPath { get; set; }
    public string? MsfPath { get; set; }
}

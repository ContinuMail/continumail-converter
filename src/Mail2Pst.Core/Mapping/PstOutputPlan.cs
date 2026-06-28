// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Mapping;

public class PstOutputPlan
{
    public string Name { get; set; } = string.Empty;
    public long MaxSizeBytes { get; set; }

    // When true (default), a source with zero messages still produces an empty
    // folder in the PST. When false, empty sources are dropped.
    public bool IncludeEmptyFolders { get; set; } = true;

    public List<SourceMapping> SourceMappings { get; set; } = new();
    public List<ContactMapping> ContactMappings { get; set; } = new();
}

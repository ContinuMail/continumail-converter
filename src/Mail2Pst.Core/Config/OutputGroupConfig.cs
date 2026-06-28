// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Config;

public class OutputGroupConfig
{
    public string Name { get; set; } = string.Empty;
    public long MaxSizeMB { get; set; } = 20000;
    public FolderMappingMode FolderMapping { get; set; } = FolderMappingMode.Mirror;

    // When true (default), empty source files still produce empty folders in the
    // PST. A GUI/CLI can surface this as a per-conversion choice.
    public bool IncludeEmptyFolders { get; set; } = true;

    public List<SourceConfig> Sources { get; set; } = new();
    public List<ContactSourceConfig> Contacts { get; set; } = new();
}

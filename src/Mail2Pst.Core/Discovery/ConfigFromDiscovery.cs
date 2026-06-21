// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;

namespace Mail2Pst.Core.Discovery;

/// <summary>
/// Builds a ConversionConfig from discovery for profile-mode conversion. With no template, uses
/// defaults. With a template, copies OPTIONS only (top-level + at most one output group's settings);
/// the template's sources are discarded — discovery supplies the sources. Reused by the SP4c GUI.
/// </summary>
public static class ConfigFromDiscovery
{
    public static ConversionConfig Build(DiscoveryResult discovery, ConversionConfig? template)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        var config = new ConversionConfig();
        var group = new OutputGroupConfig();

        if (template is not null)
        {
            if (template.Outputs.Count > 1)
                throw new ConfigValidationException(
                    "Profile mode accepts a config template with at most one output group; " +
                    $"found {template.Outputs.Count}.");

            config.JunkHandling = template.JunkHandling;

            if (template.Outputs.Count == 1)
            {
                OutputGroupConfig t = template.Outputs[0];
                group.Name = t.Name;
                group.MaxSizeMB = t.MaxSizeMB;
                group.IncludeEmptyFolders = t.IncludeEmptyFolders;
                group.FolderMapping = t.FolderMapping;
            }
        }

        if (string.IsNullOrWhiteSpace(group.Name))
            group.Name = DeriveName(discovery.Root);

        group.Sources = discovery.Sources.Select(s => new SourceConfig
        {
            Path = s.Path,
            Type = s.Type,
            TargetFolderPath = s.TargetFolderPath.ToList(),
            MsfPath = s.MsfPath,
        }).ToList();

        config.Outputs.Add(group);
        return config;
    }

    private static string DeriveName(string root)
    {
        string name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? "Thunderbird" : name;
    }
}

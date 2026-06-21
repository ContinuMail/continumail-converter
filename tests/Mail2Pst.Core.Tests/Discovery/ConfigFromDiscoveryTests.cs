// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class ConfigFromDiscoveryTests
{
    private static DiscoveryResult Discovery() => new(
        Root: "/home/user/profile",
        Layout: "thunderbird-profile",
        Sources: new[]
        {
            new DiscoveredSource("/p/Inbox", "mbox", new[] { "Local Folders", "Inbox" }, "Inbox", 10, "/p/Inbox.msf"),
            new DiscoveredSource("/p/Sent", "mbox", new[] { "Local Folders", "Sent" }, "Sent", 5, null),
        },
        Warnings: Array.Empty<DiscoveryWarning>(),
        Skipped: Array.Empty<DiscoverySkipped>(),
        Pairing: new DiscoveryPairingSummary(1, 1, 0));

    [Fact]
    public void Build_NoTemplate_UsesDefaults_MapsSourcesWithMsfPath()
    {
        ConversionConfig cfg = ConfigFromDiscovery.Build(Discovery(), null);
        OutputGroupConfig g = Assert.Single(cfg.Outputs);
        Assert.Equal("profile", g.Name);                 // derived from Root directory name
        Assert.Equal(JunkHandlingMode.Off, cfg.JunkHandling);
        Assert.Equal(2, g.Sources.Count);
        Assert.Equal("/p/Inbox.msf", g.Sources[0].MsfPath);
        Assert.Null(g.Sources[1].MsfPath);
        Assert.Equal(new List<string> { "Local Folders", "Inbox" }, g.Sources[0].TargetFolderPath);
    }

    [Fact]
    public void Build_WithTemplate_CopiesOptions_DiscardsTemplateSources()
    {
        var template = new ConversionConfig
        {
            JunkHandling = JunkHandlingMode.Category,
            Outputs =
            {
                new OutputGroupConfig
                {
                    Name = "MyArchive", MaxSizeMB = 1234, IncludeEmptyFolders = false,
                    FolderMapping = FolderMappingMode.Flatten,
                    Sources = { new SourceConfig { Path = "ignored", Type = "mbox" } },
                },
            },
        };
        ConversionConfig cfg = ConfigFromDiscovery.Build(Discovery(), template);
        OutputGroupConfig g = Assert.Single(cfg.Outputs);
        Assert.Equal("MyArchive", g.Name);
        Assert.Equal(1234, g.MaxSizeMB);
        Assert.False(g.IncludeEmptyFolders);
        Assert.Equal(JunkHandlingMode.Category, cfg.JunkHandling);
        Assert.Equal(2, g.Sources.Count);               // from discovery, not the template's 1
        Assert.Equal("/p/Inbox", g.Sources[0].Path);
    }

    [Fact]
    public void Build_TemplateWithMultipleOutputGroups_Throws()
    {
        var template = new ConversionConfig
        {
            Outputs = { new OutputGroupConfig { Name = "A" }, new OutputGroupConfig { Name = "B" } },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigFromDiscovery.Build(Discovery(), template));
    }
}

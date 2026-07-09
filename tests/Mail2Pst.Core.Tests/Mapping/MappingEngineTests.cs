// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Core.Tests.Mapping;

public class MappingEngineTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildPlan_CarriesIncludeEmptyFoldersFlag(bool includeEmpty)
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Personal",
                    MaxSizeMB = 100,
                    IncludeEmptyFolders = includeEmpty,
                    Sources = new List<SourceConfig> { new() { Path = "Inbox.mbox", Type = "mbox" } },
                },
            },
        };

        List<PstOutputPlan> plans = MappingEngine.BuildPlan(config);

        Assert.Equal(includeEmpty, plans[0].IncludeEmptyFolders);
    }

    [Fact]
    public void BuildPlan_MirrorUsesSourceFileNameAsFolder()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Personal",
                    MaxSizeMB = 100,
                    FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "extracted/Inbox.mbox", Type = "mbox" },
                    },
                },
            },
        };

        List<PstOutputPlan> plans = MappingEngine.BuildPlan(config);

        Assert.Single(plans);
        Assert.Equal("Personal", plans[0].Name);
        Assert.Equal(100L * 1024 * 1024, plans[0].MaxSizeBytes);
        Assert.Equal(new[] { "Inbox" }, plans[0].SourceMappings[0].TargetFolderPath);
    }

    [Fact]
    public void BuildPlan_FlattenUsesDefaultFolderNameForAllSources()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Archive",
                    FolderMapping = FolderMappingMode.Flatten,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "extracted/old1.mbox", Type = "mbox" },
                        new() { Path = "extracted/old2.mbox", Type = "mbox" },
                    },
                },
            },
        };

        List<PstOutputPlan> plans = MappingEngine.BuildPlan(config);

        Assert.All(plans[0].SourceMappings, m => Assert.Equal(new[] { "Imported Mail" }, m.TargetFolderPath));
    }

    [Fact]
    public void BuildPlan_TargetFolderOverrideTakesPrecedenceOverMappingMode()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Personal",
                    FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "extracted/Sent.mbox", Type = "mbox", TargetFolder = "Sent Items" },
                    },
                },
            },
        };

        List<PstOutputPlan> plans = MappingEngine.BuildPlan(config);

        Assert.Equal(new[] { "Sent Items" }, plans[0].SourceMappings[0].TargetFolderPath);
    }

    [Fact]
    public void Mirror_NoExplicitTarget_UsesFilenameStem()
    {
        var config = OneOutput(FolderMappingMode.Mirror, new SourceConfig { Type = "mbox", Path = "C:/x/Inbox.mbox" });
        var plans = MappingEngine.BuildPlan(config);
        Assert.Equal(new[] { "Inbox" }, plans[0].SourceMappings[0].TargetFolderPath);
    }

    [Fact]
    public void Flatten_NoExplicitTarget_UsesImportedMail()
    {
        var config = OneOutput(FolderMappingMode.Flatten, new SourceConfig { Type = "mbox", Path = "C:/x/Inbox.mbox" });
        var plans = MappingEngine.BuildPlan(config);
        Assert.Equal(new[] { "Imported Mail" }, plans[0].SourceMappings[0].TargetFolderPath);
    }

    [Fact]
    public void TargetFolder_NormalizesToSingleSegment()
    {
        var config = OneOutput(FolderMappingMode.Mirror,
            new SourceConfig { Type = "mbox", Path = "a.mbox", TargetFolder = "Sent" });
        var plans = MappingEngine.BuildPlan(config);
        Assert.Equal(new[] { "Sent" }, plans[0].SourceMappings[0].TargetFolderPath);
    }

    [Theory]
    [InlineData(FolderMappingMode.Mirror)]
    [InlineData(FolderMappingMode.Flatten)]
    public void ExplicitPath_WinsOverMode(FolderMappingMode mode)
    {
        var config = OneOutput(mode,
            new SourceConfig { Type = "mbox", Path = "a.mbox", TargetFolderPath = new() { "A", "B" } });
        var plans = MappingEngine.BuildPlan(config);
        Assert.Equal(new[] { "A", "B" }, plans[0].SourceMappings[0].TargetFolderPath);
    }

    [Fact]
    public void Flatten_MixedExplicitAndDefault_EachResolvesIndependently()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Flatten,
                    Sources = new List<SourceConfig>
                    {
                        new() { Type = "mbox", Path = "s1.mbox", TargetFolderPath = new() { "A", "Inbox" } },
                        new() { Type = "mbox", Path = "s2.mbox" },
                    },
                },
            },
        };
        var plans = MappingEngine.BuildPlan(config);
        Assert.Equal(new[] { "A", "Inbox" }, plans[0].SourceMappings[0].TargetFolderPath);
        Assert.Equal(new[] { "Imported Mail" }, plans[0].SourceMappings[1].TargetFolderPath);
    }

    private static ConversionConfig OneOutput(FolderMappingMode mode, params SourceConfig[] sources) => new()
    {
        Outputs = new List<OutputGroupConfig>
        {
            new() { Name = "Out", MaxSizeMB = 100, FolderMapping = mode, Sources = new List<SourceConfig>(sources) },
        },
    };

    [Fact]
    public void BuildPlan_MirrorDottedExtensionlessNames_DoNotSilentlyMerge()
    {
        // "Project.v1" and "Project.v2" both reduce to stem "Project" via GetFileNameWithoutExtension.
        // Mirror mode must keep them distinct (Project / Project (2)) with a planning warning, not merge.
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "/mail/Project.v1", Type = "mbox" },
                        new() { Path = "/mail/Project.v2", Type = "mbox" },
                    },
                },
            },
        };

        List<PstOutputPlan> plans = MappingEngine.BuildPlan(config);

        PstOutputPlan plan = Assert.Single(plans);
        Assert.Equal(new[] { "Project" },     plan.SourceMappings[0].TargetFolderPath.ToArray());
        Assert.Equal(new[] { "Project (2)" }, plan.SourceMappings[1].TargetFolderPath.ToArray());
        Assert.Single(plan.PlanningWarnings);
        Assert.Contains("Project (2)", plan.PlanningWarnings[0]);
    }

    [Fact]
    public void BuildPlan_MirrorEmptyStem_FallsBackToValidName_AndWarns()
    {
        // "/mail/.mbox" -> GetFileNameWithoutExtension -> "" -> must not become an empty folder segment;
        // it falls back to "Folder" AND records a planning warning (no silent rename).
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new() { Name = "Out", FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig> { new() { Path = "/mail/.mbox", Type = "mbox" } } },
            },
        };

        PstOutputPlan plan = Assert.Single(MappingEngine.BuildPlan(config));
        Assert.Equal(new[] { "Folder" }, plan.SourceMappings[0].TargetFolderPath.ToArray());
        Assert.Single(plan.PlanningWarnings);
    }

    [Fact]
    public void BuildPlan_ExplicitTargetFolderCollisions_AreNotDisambiguated()
    {
        // Two sources explicitly targeting the same folder is the user's choice — keep both as-is
        // (an intentional merge), no disambiguation, no planning warning. Guards the "mirror-only" rule.
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new() { Name = "Out", FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "/a/x", Type = "mbox", TargetFolder = "Shared" },
                        new() { Path = "/b/y", Type = "mbox", TargetFolder = "Shared" },
                    } },
            },
        };

        PstOutputPlan plan = Assert.Single(MappingEngine.BuildPlan(config));
        Assert.Equal(new[] { "Shared" }, plan.SourceMappings[0].TargetFolderPath.ToArray());
        Assert.Equal(new[] { "Shared" }, plan.SourceMappings[1].TargetFolderPath.ToArray());   // NOT "Shared (2)"
        Assert.Empty(plan.PlanningWarnings);
    }
}

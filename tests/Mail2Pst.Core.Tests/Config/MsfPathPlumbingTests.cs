// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class MsfPathPlumbingTests
{
    private static ConversionConfig Config(string? msfPath) => new()
    {
        Outputs = new List<OutputGroupConfig>
        {
            new() { Name = "P", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                Sources = new List<SourceConfig>
                {
                    new() { Type = "mbox", Path = "Inbox", MsfPath = msfPath,
                            TargetFolderPath = new List<string> { "Inbox" } },
                } },
        },
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Inbox.msf")]
    public void ConfigValidator_AcceptsAnyMsfPath_NoThrow(string? msfPath)
        => ConfigValidator.Validate(Config(msfPath)); // lenient: must not throw

    [Fact]
    public void MsfPath_FlowsThrough_MappingEngine_ToSourceMapping()
    {
        List<PstOutputPlan> plans = MappingEngine.BuildPlan(Config("Inbox.msf"));
        SourceMapping m = plans.Single().SourceMappings.Single();
        Assert.Equal("Inbox.msf", m.Source.MsfPath);
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Core.Tests.Mapping;

public class MappingEngineContactTests
{
    [Fact]
    public void BuildPlan_ContactDefaultFolder_IsContactsSlashBookName()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new() { Name = "Out", Sources = new List<SourceConfig>(),
                    Contacts = new List<ContactSourceConfig>
                    {
                        new() { Path = "/p/abook.sqlite", Format = "thunderbird-sqlite" },
                    } },
            },
        };
        PstOutputPlan plan = MappingEngine.BuildPlan(config).Single();
        ContactMapping cm = plan.ContactMappings.Single();
        Assert.Equal(new[] { "Contacts", "abook" }, cm.TargetFolderPath);
    }

    [Fact]
    public void BuildPlan_ContactPathCollidesWithMailFolder_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out",
                    FolderMapping = FolderMappingMode.Flatten,
                    Sources = new List<SourceConfig>
                    {
                        new() { Path = "/p/x.mbox", Type = "mbox",
                                TargetFolderPath = new List<string> { "Contacts", "abook" } },
                    },
                    Contacts = new List<ContactSourceConfig>
                    {
                        new() { Path = "/p/abook.sqlite", Format = "thunderbird-sqlite",
                                TargetFolderPath = new[] { "Contacts", "abook" } },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => MappingEngine.BuildPlan(config));
    }
}

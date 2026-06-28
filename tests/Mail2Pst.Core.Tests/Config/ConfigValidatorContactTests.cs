// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class ConfigValidatorContactTests
{
    [Fact]
    public void Validate_ContactOnlyOutput_IsLegal()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Contacts",
                    Sources = new List<SourceConfig>(),
                    Contacts = new List<ContactSourceConfig>
                    {
                        new() { Path = "abook.sqlite", Format = "thunderbird-sqlite",
                                TargetFolderPath = new[] { "Contacts", "Personal" } },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_OutputWithNoSourcesAndNoContacts_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig> { new() { Name = "Empty", Sources = new List<SourceConfig>() } },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_UnknownContactFormat_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new() { Name = "X", Sources = new List<SourceConfig>(),
                        Contacts = new List<ContactSourceConfig> { new() { Path = "a", Format = "bogus" } } },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }
}

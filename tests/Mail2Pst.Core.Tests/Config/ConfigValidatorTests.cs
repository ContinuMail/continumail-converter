// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class ConfigValidatorTests
{
    private static string SampleMbox => Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.mbox");

    private static ConversionConfig ValidConfig() => new()
    {
        Outputs =
        {
            new OutputGroupConfig
            {
                Name = "Personal",
                MaxSizeMB = 100,
                Sources = { new SourceConfig { Path = SampleMbox, Type = "mbox" } },
            },
        },
    };

    [Fact]
    public void Validate_ValidConfig_DoesNotThrow()
    {
        ConfigValidator.Validate(ValidConfig());
    }

    [Fact]
    public void Validate_NoOutputs_Throws()
    {
        var config = new ConversionConfig();
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_DuplicateOutputNamesCaseInsensitive_Throws()
    {
        var config = ValidConfig();
        config.Outputs.Add(new OutputGroupConfig
        {
            Name = "personal", // same as "Personal" on a case-insensitive filesystem
            MaxSizeMB = 100,
            Sources = { new SourceConfig { Path = SampleMbox, Type = "mbox" } },
        });
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_NonPositiveMaxSize_Throws(long maxSizeMB)
    {
        var config = ValidConfig();
        config.Outputs[0].MaxSizeMB = maxSizeMB;
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData(51201)]              // one MB above the 50 GB product cap
    [InlineData(long.MaxValue)]      // would overflow MB -> bytes
    public void Validate_MaxSizeMBAboveCap_Throws(long maxSizeMb)
    {
        var config = ValidConfig();
        config.Outputs[0].MaxSizeMB = maxSizeMb;

        var ex = Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
        Assert.Contains("maxSizeMB", ex.Message);
    }

    [Fact]
    public void Validate_OutputWithNoSources_Throws()
    {
        var config = ValidConfig();
        config.Outputs[0].Sources = new List<SourceConfig>();
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_SourceWithEmptyPath_Throws()
    {
        var config = ValidConfig();
        config.Outputs[0].Sources[0].Path = "";
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_InvalidOutputName_Throws()
    {
        var config = ValidConfig();
        config.Outputs[0].Name = "../escape";
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_InvalidTargetFolder_Throws()
    {
        var config = ValidConfig();
        config.Outputs[0].Sources[0].TargetFolder = "bad/name";
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_NullTargetFolder_DoesNotThrow()
    {
        var config = ValidConfig();
        config.Outputs[0].Sources[0].TargetFolder = null;
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_ValidTargetFolder_DoesNotThrow()
    {
        var config = ValidConfig();
        config.Outputs[0].Sources[0].TargetFolder = "Work Mail";
        ConfigValidator.Validate(config); // must not throw
    }

    [Fact]
    public void Validate_SourceWithBothTargetFolderAndPath_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Type = "mbox", Path = "a.mbox", TargetFolder = "X", TargetFolderPath = new() { "A", "B" } },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_SourceWithInvalidPathSegment_Throws()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Type = "mbox", Path = "a.mbox", TargetFolderPath = new() { "A", "B/C" } },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_SourceWithEmptyTargetFolder_Throws() // explicit-but-invalid flat target surfaces
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Type = "mbox", Path = "a.mbox", TargetFolder = "" },
                    },
                },
            },
        };
        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_SourceWithValidNestedPath_DoesNotThrow()
    {
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Out", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig>
                    {
                        new() { Type = "mbox", Path = "a.mbox", TargetFolderPath = new() { "A", "B" } },
                    },
                },
            },
        };
        ConfigValidator.Validate(config); // does not throw
    }
}

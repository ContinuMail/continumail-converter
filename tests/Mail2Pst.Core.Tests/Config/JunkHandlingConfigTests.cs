// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class JunkHandlingConfigTests
{
    private static ConversionConfig Base(JunkHandlingMode junk) => new()
    {
        JunkHandling = junk,
        Outputs = new List<OutputGroupConfig>
        {
            new() { Name = "P", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig> { new() { Type = "mbox", Path = "x.mbox" } } },
        },
    };

    [Fact]
    public void Default_IsOff()
        => Assert.Equal(JunkHandlingMode.Off, new ConversionConfig().JunkHandling);

    [Theory]
    [InlineData(JunkHandlingMode.Off)]
    [InlineData(JunkHandlingMode.Category)]
    public void OffAndCategory_AreAccepted(JunkHandlingMode junk)
        => ConfigValidator.Validate(Base(junk)); // does not throw

    [Fact]
    public void Folder_IsRejected_UntilSp4()
    {
        var ex = Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(Base(JunkHandlingMode.Folder)));
        Assert.Contains("Folder", ex.Message);
    }
}

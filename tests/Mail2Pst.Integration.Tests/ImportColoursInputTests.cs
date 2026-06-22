// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class ImportColoursInputTests
{
    [Fact]
    public void ParsesProfileAndApply()
    {
        var input = ImportColoursInput.Parse(new[] { "--profile", "/p", "--apply" });
        Assert.Null(input.Error);
        Assert.Equal("/p", input.ProfilePath);
        Assert.True(input.Apply);
    }

    [Fact]
    public void DefaultsToPreview_WhenNoApply()
    {
        var input = ImportColoursInput.Parse(new[] { "--profile", "/p" });
        Assert.Null(input.Error);
        Assert.False(input.Apply);
    }

    [Fact]
    public void MissingProfile_IsError()
    {
        var input = ImportColoursInput.Parse(new[] { "--apply" });
        Assert.NotNull(input.Error);
    }

    [Fact]
    public void UnknownFlag_IsError()
    {
        var input = ImportColoursInput.Parse(new[] { "--profile", "/p", "--bogus" });
        Assert.NotNull(input.Error);
    }

    [Fact]
    public void ProfileWithoutValue_IsError()
    {
        var input = ImportColoursInput.Parse(new[] { "--profile", "--apply" });
        Assert.NotNull(input.Error);
    }

    [Fact]
    public void ProfileFlagLast_NoValue_IsError()
    {
        var input = ImportColoursInput.Parse(new[] { "--profile" });
        Assert.NotNull(input.Error);
    }

    [Fact]
    public void Parse_PlanFile_IsAccepted()
    {
        var input = ImportColoursInput.Parse(new[] { "--plan-file", "C:\\tmp\\plan.json" });
        Assert.Null(input.Error);
        Assert.Equal("C:\\tmp\\plan.json", input.PlanFile);
        Assert.Null(input.ProfilePath);
    }

    [Fact]
    public void Parse_PlanFileAndProfile_IsRejected()
    {
        var input = ImportColoursInput.Parse(new[] { "--plan-file", "p.json", "--profile", "d" });
        Assert.NotNull(input.Error);
    }

    [Fact]
    public void Parse_PlanFileWithoutValue_IsRejected()
    {
        var input = ImportColoursInput.Parse(new[] { "--plan-file" });
        Assert.NotNull(input.Error);
    }
}

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
}

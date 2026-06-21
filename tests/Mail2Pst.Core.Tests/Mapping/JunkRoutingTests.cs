// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Core.Tests.Mapping;

public class JunkRoutingTests
{
    private static readonly string[] Mapped = { "Accounts", "Work", "Inbox" };

    [Theory]
    [InlineData(JunkHandlingMode.Off, true)]
    [InlineData(JunkHandlingMode.Off, false)]
    [InlineData(JunkHandlingMode.Category, true)]
    [InlineData(JunkHandlingMode.Category, false)]
    [InlineData(JunkHandlingMode.Folder, false)]
    public void NonRouting_ReturnsMappedPathUnchanged(JunkHandlingMode mode, bool isJunk)
    {
        var result = JunkRouting.ResolveTargetFolderPath(Mapped, isJunk, mode);
        Assert.Same(Mapped, result); // same reference, no allocation on the common path
    }

    [Fact]
    public void FolderModeAndJunk_RoutesToJunkEmail()
    {
        var result = JunkRouting.ResolveTargetFolderPath(Mapped, isJunk: true, JunkHandlingMode.Folder);
        Assert.Equal(new[] { "Junk Email" }, result);
    }

    [Fact]
    public void DefaultJunkFolderName_IsJunkEmail()
        => Assert.Equal("Junk Email", JunkRouting.DefaultJunkFolderName);
}

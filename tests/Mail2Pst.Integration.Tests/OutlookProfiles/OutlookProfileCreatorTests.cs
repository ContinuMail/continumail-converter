// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

// Covers ONLY the branches that do not spawn Outlook. A name that already exists in the fake
// registry short-circuits before Process.Start; an invalid name throws before that too. Never
// exercise the spawn path here (see brief: a non-existent name against a fake reg that never
// reports it appearing would spawn Outlook and hang — that path is owner-run E2E only, Task 7).
public class OutlookProfileCreatorTests
{
    private sealed class FakeReg : IRegistryKeyReader
    {
        public string[] Subkeys = Array.Empty<string>();
        public string? Default;
        public string[] SubKeyNames(string path) => path == OutlookProfileRegistry.ProfilesKey ? Subkeys : Array.Empty<string>();
        public string? StringValue(string path, string name) =>
            path == OutlookProfileRegistry.OutlookKey && name == "DefaultProfile" ? Default : null;
    }

    [Fact]
    public void EnsureProfile_ExistingName_ReusesWithoutSpawning()
    {
        var reg = new FakeReg { Subkeys = new[] { "ContinuMail", "Work" } };
        (bool created, bool reused) = OutlookProfileCreator.EnsureProfile("ContinuMail", reg);
        Assert.False(created);
        Assert.True(reused);
    }

    [Fact]
    public void EnsureProfile_ExistingName_IsCaseInsensitive()
    {
        var reg = new FakeReg { Subkeys = new[] { "ContinuMail" } };
        (bool created, bool reused) = OutlookProfileCreator.EnsureProfile("continumail", reg);
        Assert.False(created);
        Assert.True(reused);
    }

    [Fact]
    public void EnsureProfile_InvalidName_ThrowsBeforeTouchingRegistryOrSpawning()
    {
        var reg = new FakeReg();
        var ex = Assert.Throws<InvalidOperationException>(() => OutlookProfileCreator.EnsureProfile(@"bad\name", reg));
        Assert.StartsWith("invalid-profile-name:", ex.Message);
    }

    [Fact]
    public void EnsureProfile_EmptyName_ThrowsInvalidProfileName()
    {
        var reg = new FakeReg();
        var ex = Assert.Throws<InvalidOperationException>(() => OutlookProfileCreator.EnsureProfile("", reg));
        Assert.StartsWith("invalid-profile-name:", ex.Message);
    }

    [Fact]
    public void ResolveOutlookExe_ReturnsNonEmptyString()
    {
        string exe = OutlookProfileCreator.ResolveOutlookExe();
        Assert.False(string.IsNullOrWhiteSpace(exe));
    }
}

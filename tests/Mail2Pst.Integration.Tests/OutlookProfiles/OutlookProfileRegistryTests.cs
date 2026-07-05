// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class OutlookProfileRegistryTests
{
    private sealed class FakeReg : IRegistryKeyReader
    {
        public string[] Subkeys = System.Array.Empty<string>();
        public string? Default;
        public string[] SubKeyNames(string path) => path == OutlookProfileRegistry.ProfilesKey ? Subkeys : System.Array.Empty<string>();
        public string? StringValue(string path, string name) =>
            path == OutlookProfileRegistry.OutlookKey && name == "DefaultProfile" ? Default : null;
    }

    [Fact]
    public void Reads_profiles_and_default()
    {
        var info = OutlookProfileRegistry.Read(new FakeReg { Subkeys = new[] { "Colour", "Work" }, Default = "Work" });
        Assert.Equal(new[] { "Colour", "Work" }, info.Profiles);
        Assert.Equal("Work", info.DefaultProfile);
    }

    [Fact]
    public void Missing_hive_yields_empty_not_error()
    {
        var info = OutlookProfileRegistry.Read(new FakeReg());
        Assert.Empty(info.Profiles);
        Assert.Null(info.DefaultProfile);
    }

    [Fact]
    public void Default_not_in_list_is_surfaced_verbatim()
    {
        // Resolver (Task 2) decides validity; the registry layer reports raw facts.
        var info = OutlookProfileRegistry.Read(new FakeReg { Subkeys = new[] { "Colour" }, Default = "Ghost" });
        Assert.Equal("Ghost", info.DefaultProfile);
    }
}

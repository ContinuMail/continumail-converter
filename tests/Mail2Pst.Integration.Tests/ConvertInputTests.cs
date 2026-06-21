// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Cli;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class ConvertInputTests
{
    // Builds a minimal Thunderbird profile dir: <p>/Mail/Local Folders/{Inbox, Inbox.msf}.
    private static string MakeProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "mail2pst-prof-" + Guid.NewGuid());
        string acct = Path.Combine(root, "Mail", "Local Folders");
        Directory.CreateDirectory(acct);
        File.WriteAllText(Path.Combine(acct, "Inbox"), "");            // 0-byte = valid empty folder
        File.WriteAllText(Path.Combine(acct, "Inbox.msf"), "x");       // existence-only pairing
        return root;
    }

    [Fact]
    public void Resolve_MissingOutput_Error()
    {
        ConvertResolution r = ConvertInput.Resolve(new[] { "--config", "c.json" });
        Assert.NotNull(r.Error);
        Assert.Null(r.Config);
    }

    [Fact]
    public void Resolve_NeitherConfigNorProfile_Error()
    {
        ConvertResolution r = ConvertInput.Resolve(new[] { "--output", "out" });
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Resolve_ProfileOnly_BuildsConfigWithDiscoveredSources()
    {
        string profile = MakeProfile();
        try
        {
            ConvertResolution r = ConvertInput.Resolve(new[] { "--profile", profile, "--output", "out" });
            Assert.Null(r.Error);
            Assert.NotNull(r.Config);
            OutputGroupConfig g = Assert.Single(r.Config!.Outputs);
            SourceConfig s = Assert.Single(g.Sources);
            Assert.EndsWith("Inbox.msf", s.MsfPath);
            Assert.Equal(profile, r.InputLabel);
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public void Resolve_ProfileWithMultiGroupTemplate_Error()
    {
        string profile = MakeProfile();
        string cfg = Path.Combine(Path.GetTempPath(), "mail2pst-tmpl-" + Guid.NewGuid() + ".json");
        File.WriteAllText(cfg, "{\"outputs\":[{\"name\":\"A\",\"sources\":[]},{\"name\":\"B\",\"sources\":[]}]}");
        try
        {
            ConvertResolution r = ConvertInput.Resolve(new[] { "--profile", profile, "--config", cfg, "--output", "out" });
            Assert.NotNull(r.Error);
            Assert.Null(r.Config);
        }
        finally { Directory.Delete(profile, true); File.Delete(cfg); }
    }

    [Fact]
    public void Resolve_ProfileDirMissing_Error()
    {
        ConvertResolution r = ConvertInput.Resolve(new[] { "--profile", "no-such-dir", "--output", "out" });
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Resolve_UnknownOption_Error()
    {
        ConvertResolution r = ConvertInput.Resolve(new[] { "--config", "c.json", "--output", "out", "--weird", "x" });
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Resolve_ExtraPositionalArg_Error()
    {
        ConvertResolution r = ConvertInput.Resolve(new[] { "--config", "c.json", "--output", "out", "extra" });
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Resolve_FlagMissingValue_Error()
    {
        ConvertResolution r = ConvertInput.Resolve(new[] { "--config", "--output", "out" });
        Assert.NotNull(r.Error);
    }
}

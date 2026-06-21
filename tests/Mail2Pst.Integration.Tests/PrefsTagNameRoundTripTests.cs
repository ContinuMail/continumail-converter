// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class PrefsTagNameRoundTripTests
{
    // .msf: message-id=a@h, keywords="$label1 custom" ($ is Mork-escaped as $24).
    private const string MsfText =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)" +
        "(89=message-id)(8B=keywords) >\n" +
        "{1:^80 {(k^96:c)} [1(^89=a@h)(^8B=$24label1 custom)] }";

    private static string Msg(string id) =>
        $"From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: {id}\nSubject: t\n\nbody\n";

    private static string NewDir(string tag)
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-" + tag + "-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    private static IReadOnlyList<string> Convert(string? profilePath)
    {
        string profileDir = profilePath ?? NewDir("noprof");
        string mbox = Path.Combine(profileDir, "Inbox");
        File.WriteAllText(mbox, Msg("<a@h>"));
        File.WriteAllText(Path.Combine(profileDir, "Inbox.msf"), MsfText);
        string outDir = NewDir("out");
        string template = TemplateProvider.ExtractToTempFile();
        try
        {
            var config = new ConversionConfig
            {
                ProfilePath = profilePath,
                Outputs =
                {
                    new OutputGroupConfig
                    {
                        Name = "Out",
                        Sources = { new SourceConfig
                            { Path = mbox, Type = "mbox", MsfPath = Path.Combine(profileDir, "Inbox.msf"),
                              TargetFolder = "Inbox" } },
                    },
                },
            };
            ConversionReport report = new ConversionRunner(template).Run(config, outDir);
            ReadBackMessage m = PstReader.Read(report.OutputFiles).SelectMany(f => f.Messages).Single();
            return m.Categories;
        }
        finally
        {
            File.Delete(template);
            Directory.Delete(outDir, true);
            if (profilePath is null) Directory.Delete(profileDir, true);
        }
    }

    [Fact]
    public void PrefsJs_RenamedBuiltinAndCustom_AppliedToCategories()
    {
        string profile = NewDir("prof");
        File.WriteAllText(Path.Combine(profile, "prefs.js"),
            "user_pref(\"mailnews.tags.$label1.tag\", \"Critique\");\n" +
            "user_pref(\"mailnews.tags.custom.tag\", \"Client X\");\n");
        try
        {
            IReadOnlyList<string> categories = Convert(profile);
            Assert.Equal(new[] { "Critique", "Client X" }, categories);
        }
        finally { Directory.Delete(profile, true); }
    }

    [Fact]
    public void NoPrefsJs_FallsBackToBuiltinNames()
    {
        // No ProfilePath -> DefaultMsfTagResolver: $label1 -> "Important", custom passes through.
        IReadOnlyList<string> categories = Convert(profilePath: null);
        Assert.Equal(new[] { "Important", "custom" }, categories);
    }
}

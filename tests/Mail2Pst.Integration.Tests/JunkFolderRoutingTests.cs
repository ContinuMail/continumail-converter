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

public class JunkFolderRoutingTests
{
    // .msf: message-id=a@h, flags=5 (read+marked), junkscore=90 (junk), keywords=work.
    private const string MsfText =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)" +
        "(88=flags)(89=message-id)(8A=junkscore)(8B=keywords) >\n" +
        "{1:^80 {(k^96:c)} [1(^88=5)(^89=a@h)(^8A=90)(^8B=work)] }";

    private static string Msg(string id, string body) =>
        $"From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: {id}\nSubject: t\n\n{body}\n";

    private static string WriteTemp(string content, string ext)
    {
        string p = Path.Combine(Path.GetTempPath(), "mail2pst-jfr-" + Guid.NewGuid() + ext);
        File.WriteAllText(p, content);
        return p;
    }

    private static string NewOutDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-jfro-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    private static (IReadOnlyList<ReadFolder> folders, ConversionReport report) Convert(
        string mboxPath, string? msfPath, string outDir, JunkHandlingMode junk)
    {
        string template = TemplateProvider.ExtractToTempFile();
        try
        {
            var config = new ConversionConfig
            {
                JunkHandling = junk,
                Outputs =
                {
                    new OutputGroupConfig
                    {
                        Name = "Out",
                        Sources = { new SourceConfig
                            { Path = mboxPath, Type = "mbox", MsfPath = msfPath, TargetFolder = "Inbox" } },
                    },
                },
            };
            var runner = new ConversionRunner(template);
            ConversionReport report = runner.Run(config, outDir);
            return (PstReader.Read(report.OutputFiles), report);
        }
        finally { File.Delete(template); }
    }

    [Fact]
    public void FolderMode_RoutesJunkToJunkEmail_PreservesTags_NonJunkStays()
    {
        // <a@h> = junk (junkscore 90) + keyword "work"; <b@h> = no .msf row -> not junk.
        string mbox = WriteTemp(Msg("<a@h>", "one") + Msg("<b@h>", "two"), ".mbox");
        string msf = WriteTemp(MsfText, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (folders, _) = Convert(mbox, msf, outDir, JunkHandlingMode.Folder);

            ReadFolder junk = folders.Single(f => f.DisplayPath == "Junk Email");
            ReadFolder inbox = folders.Single(f => f.DisplayPath == "Inbox");

            ReadBackMessage routed = Assert.Single(junk.Messages);
            Assert.Equal("<a@h>", routed.MessageId);
            Assert.True(routed.IsRead);                        // .msf flags=5 applied -> enrich ran BEFORE routing
            Assert.True(routed.IsFlagged);
            Assert.Contains("work", routed.Categories);       // user tag preserved
            Assert.DoesNotContain("Junk", routed.Categories); // synthetic category suppressed in Folder mode

            ReadBackMessage stayed = Assert.Single(inbox.Messages);
            Assert.Equal("<b@h>", stayed.MessageId);
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }
}

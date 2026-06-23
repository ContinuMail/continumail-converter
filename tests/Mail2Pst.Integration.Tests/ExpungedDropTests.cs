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

public class ExpungedDropTests
{
    // .msf: message-id=a@h, flags=8 (expunged). One column header, one row.
    private const string MsfExpungedA =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(89=message-id) >\n" +
        "{1:^80 {(k^96:c)} [1(^88=8)(^89=a@h)] }";

    // .msf: message-id=c@h, flags=8 (expunged).
    private const string MsfExpungedC =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(89=message-id) >\n" +
        "{1:^80 {(k^96:c)} [1(^88=8)(^89=c@h)] }";

    private static string Msg(string id, string body) =>
        $"From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: {id}\nSubject: t\n\n{body}\n";

    private static string MsgWithAttachment(string id) =>
        "From s@e Thu Jan 01 00:00:00 2026\n" +
        $"Message-ID: {id}\n" +
        "Subject: t\n" +
        "MIME-Version: 1.0\n" +
        "Content-Type: multipart/mixed; boundary=\"b\"\n\n" +
        "--b\nContent-Type: text/plain\n\nbody\n" +
        "--b\nContent-Type: application/octet-stream\n" +
        "Content-Disposition: attachment; filename=\"f.bin\"\n" +
        "Content-Transfer-Encoding: base64\n\nZGF0YQ==\n" +
        "--b--\n";

    private static string WriteTemp(string content, string ext)
    {
        string p = Path.Combine(Path.GetTempPath(), "mail2pst-exp-" + Guid.NewGuid() + ext);
        File.WriteAllText(p, content);
        return p;
    }

    private static string NewOutDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-expo-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    private static (IReadOnlyList<ReadBackMessage> read, ConversionReport report) Convert(
        string mboxPath, string? msfPath, string outDir, bool dropExpunged)
    {
        var config = new ConversionConfig
        {
            DropExpunged = dropExpunged,
            Outputs =
            {
                new OutputGroupConfig
                {
                    Name = "Out",
                    Sources = { new SourceConfig { Path = mboxPath, Type = "mbox", MsfPath = msfPath } },
                },
            },
        };
        ConversionReport report = new ConversionRunner().Run(config, outDir);
        return (PstReader.Read(report.OutputFiles).SelectMany(f => f.Messages).ToList(), report);
    }

    [Fact]
    public void DropExpunged_True_DropsExpungedMatch_KeepsUnmatched()
    {
        // <a@h> = expunged in .msf -> dropped; <b@h> = no .msf row -> kept.
        string mbox = WriteTemp(Msg("<a@h>", "one") + Msg("<b@h>", "two"), ".mbox");
        string msf = WriteTemp(MsfExpungedA, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, msf, outDir, dropExpunged: true);
            Assert.Single(read);
            Assert.Equal("<b@h>", read[0].MessageId);
            Assert.Equal(1, report.EnrichmentSummary.ExpungedMatched); // observability: matched + expunged
            Assert.Equal(1, report.EnrichmentSummary.ExpungedDropped); // actioned: actually suppressed
            Assert.Equal(1, report.ConvertedCount);
            Assert.Equal(0, report.SkippedCount);     // a drop is not a skip
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void DropExpunged_False_KeepsExpunged()
    {
        string mbox = WriteTemp(Msg("<a@h>", "one") + Msg("<b@h>", "two"), ".mbox");
        string msf = WriteTemp(MsfExpungedA, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, msf, outDir, dropExpunged: false);
            Assert.Equal(2, read.Count);
            Assert.Contains(read, m => m.MessageId == "<a@h>");
            Assert.Equal(0, report.EnrichmentSummary.ExpungedDropped);
            Assert.Equal(1, report.EnrichmentSummary.ExpungedMatched);
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void DropExpunged_True_WithAttachment_DisposesAndCompletes()
    {
        // The only message is expunged + has an attachment -> dropped; conversion completes,
        // and the runner disposes the dropped message's attachment stream (no leak/crash).
        string mbox = WriteTemp(MsgWithAttachment("<c@h>"), ".mbox");
        string msf = WriteTemp(MsfExpungedC, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, msf, outDir, dropExpunged: true);
            Assert.Empty(read);
            Assert.Equal(0, report.ConvertedCount);
            Assert.Equal(1, report.EnrichmentSummary.ExpungedDropped);
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class LiveOffsetFilterIntegrationTests
{
    private static string Msg(string id, string body) =>
        $"From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: {id}\nSubject: t\n\n{body}\n\n";

    private static string WriteTemp(string content, string ext)
    {
        string p = Path.Combine(Path.GetTempPath(), "mail2pst-lof-" + Guid.NewGuid() + ext);
        File.WriteAllText(p, content, new UTF8Encoding(false));
        return p;
    }
    private static string NewOutDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-lofo-" + Guid.NewGuid());
        Directory.CreateDirectory(d); return d;
    }

    [Fact]
    public void UncompactedFolder_DeadCopiesDropped_LiveKeptWithMetadata()
    {
        // 2 dead copies (low offsets) + 2 live copies (high offsets); .msf lists only the live two.
        string dead = Msg("<a@h>", "x") + Msg("<b@h>", "y");
        string liveA = Msg("<a@h>", "x");
        long offA = Encoding.ASCII.GetByteCount(dead);
        long offB = offA + Encoding.ASCII.GetByteCount(liveA);
        string mbox = WriteTemp(dead + liveA + Msg("<b@h>", "y"), ".mbox");
        // .msf: a@h read+marked(5) at offA; b@h read(1) at offB.
        string msf = WriteTemp(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(89=message-id)(CE=storeToken) >\n" +
            "{1:^80 {(k^96:c)} [1(^88=5)(^89=a@h)(^CE=" + offA + ")] [2(^88=1)(^89=b@h)(^CE=" + offB + ")] }", ".msf");
        string outDir = NewOutDir();
        try
        {
            var config = new ConversionConfig
            {
                Outputs = { new OutputGroupConfig { Name = "Out",
                    Sources = { new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf } } } },
            };
            ConversionReport report = new ConversionRunner().Run(config, outDir);
            var read = PstReader.Read(report.OutputFiles).SelectMany(f => f.Messages).ToList();

            Assert.Equal(2, read.Count);                                   // dead copies dropped
            Assert.Equal(2, report.EnrichmentSummary.OrphanedCopiesDropped);
            Assert.True(read.Single(m => m.MessageId == "<a@h>").IsFlagged); // live metadata intact
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void MisalignedMsf_NothingDropped_AllKept()
    {
        string mbox = WriteTemp(Msg("<a@h>", "x") + Msg("<a@h>", "x"), ".mbox");
        string msf = WriteTemp(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(89=message-id)(CE=storeToken) >\n" +
            "{1:^80 {(k^96:c)} [1(^88=1)(^89=a@h)(^CE=987654)] }", ".msf"); // bogus offset
        string outDir = NewOutDir();
        try
        {
            var config = new ConversionConfig
            {
                Outputs = { new OutputGroupConfig { Name = "Out",
                    Sources = { new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf } } } },
            };
            ConversionReport report = new ConversionRunner().Run(config, outDir);
            var read = PstReader.Read(report.OutputFiles).SelectMany(f => f.Messages).ToList();
            Assert.Equal(2, read.Count);                                       // kept all
            Assert.Equal(0, report.EnrichmentSummary.OrphanedCopiesDropped);
            Assert.Equal(1, report.EnrichmentSummary.LiveOffsetFilterDisabledSources);
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }
}

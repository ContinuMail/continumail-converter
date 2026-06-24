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

public class ConversionRunnerEnrichmentTests
{
    // .msf: message-id=a@h, flags=5 (read+marked), junkscore=90, keywords=work.
    private const string MsfText =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)" +
        "(88=flags)(89=message-id)(8A=junkscore)(8B=keywords) >\n" +
        "{1:^80 {(k^96:c)} [1(^88=5)(^89=a@h)(^8A=90)(^8B=work)] }";

    private static string Msg(string id, string body) =>
        $"From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: {id}\nSubject: t\n\n{body}\n";

    private static string WriteTemp(string content, string ext)
    {
        string p = Path.Combine(Path.GetTempPath(), "mail2pst-cre-" + Guid.NewGuid() + ext);
        File.WriteAllText(p, content);
        return p;
    }

    private static (IReadOnlyList<ReadBackMessage> read, ConversionReport report) Convert(
        string mboxPath, string? msfPath, string outDir, JunkHandlingMode junk = JunkHandlingMode.Off)
    {
        var config = new ConversionConfig
        {
            JunkHandling = junk,
            Outputs =
            {
                new OutputGroupConfig
                {
                    Name = "Out",
                    Sources = { new SourceConfig { Path = mboxPath, Type = "mbox", MsfPath = msfPath } },
                },
            },
        };
        var runner = new ConversionRunner();
        ConversionReport report = runner.Run(config, outDir);
        var read = PstReader.Read(report.OutputFiles).SelectMany(f => f.Messages).ToList();
        return (read, report);
    }

    private static string NewOutDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-creo-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void PairedMsf_EnrichesFlagsAndCategories()
    {
        string mbox = WriteTemp(Msg("<a@h>", "one"), ".mbox");
        string msf = WriteTemp(MsfText, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, msf, outDir, JunkHandlingMode.Category);
            ReadBackMessage m = read.Single(x => x.MessageId == "<a@h>");
            Assert.True(m.IsRead);
            Assert.True(m.IsFlagged);
            Assert.Contains("work", m.Categories);
            Assert.Contains("Junk", m.Categories); // junkscore 90 + Category mode
            Assert.Equal(1, report.EnrichmentSummary.Matched);
            Assert.Equal(1, report.EnrichmentSummary.SourcesEnriched);
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void MissingMsf_Degrades_ConversionSucceeds_NoExtraSkip()
    {
        string mbox = WriteTemp(Msg("<a@h>", "one"), ".mbox");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, "no-such.msf", outDir);
            Assert.Single(read);                      // message still converted
            Assert.Equal(0, report.SkippedCount);     // no message skips from the degradation
            Assert.Equal(1, report.EnrichmentSummary.SourcesDegraded);
            Assert.Equal(1, report.WarningCount);
        }
        finally { File.Delete(mbox); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void UnreadableMbox_WithMsf_OneSkip_NoMsfWarning_NotDegraded()
    {
        string msf = WriteTemp(MsfText, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert("no-such.mbox", msf, outDir);
            Assert.Empty(read);
            Assert.Equal(1, report.SkippedCount);     // the single source skip
            Assert.Equal(0, report.WarningCount);     // no .msf degradation warning
            Assert.Equal(1, report.EnrichmentSummary.SourcesAttempted);
            Assert.Equal(0, report.EnrichmentSummary.SourcesDegraded);
            Assert.Equal(0, report.EnrichmentSummary.SourcesEnriched);
        }
        finally { File.Delete(msf); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void NoMsf_SourceUnchanged_NoEnrichmentAttempted()
    {
        string mbox = WriteTemp(Msg("<a@h>", "one"), ".mbox");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, null, outDir);
            Assert.Single(read);
            Assert.Equal(0, report.EnrichmentSummary.SourcesAttempted);
        }
        finally { File.Delete(mbox); Directory.Delete(outDir, true); }
    }

    [Fact]
    public void MboxSideDuplicateId_StreamingParity_BothEnrichedFromUniqueMsfRow()
    {
        // Two mbox messages share <a@h> (uncompacted IMAP expunge copies); the .msf has ONE row for it.
        // That row is unambiguous, so BOTH copies are enriched (marked + tag) — the streaming path
        // mirrors batch Enrich. Regression: previously both copies were skipped, losing stars and tags.
        string mbox = WriteTemp(Msg("<a@h>", "one") + Msg("<a@h>", "two"), ".mbox");
        string msf = WriteTemp(MsfText, ".msf");
        string outDir = NewOutDir();
        try
        {
            var (read, report) = Convert(mbox, msf, outDir);
            Assert.Equal(2, read.Count);
            Assert.Equal(2, report.EnrichmentSummary.Matched);
            Assert.Equal(0, report.EnrichmentSummary.SkippedDuplicateId);
            foreach (ReadBackMessage m in read)
            {
                Assert.True(m.IsFlagged);              // marked (star) from the single .msf row
                Assert.Contains("work", m.Categories); // tag from the single .msf row
            }
        }
        finally { File.Delete(mbox); File.Delete(msf); Directory.Delete(outDir, true); }
    }
}

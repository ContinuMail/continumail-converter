// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class SourceEnrichmentContextTests
{
    private static MsfEnrichmentOptions Opts() =>
        new() { TagResolver = new DefaultMsfTagResolver(), JunkHandling = JunkHandlingMode.Off };

    // A .msf with one msgs row: message-id=a@h, flags=5 (read+marked).
    private const string MsfText =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(89=message-id) >\n" +
        "{1:^80 {(k^96:c)} [1(^88=5)(^89=a@h)] }";

    private static string Mbox() =>
        "From s@e Thu Jan 01 00:00:00 2026\nMessage-ID: <a@h>\nSubject: t\n\nbody\n";

    private static string Write(string content, string ext)
    {
        string path = Path.Combine(Path.GetTempPath(), "mail2pst-sec-" + Guid.NewGuid() + ext);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void TryCreate_BlankMsfPath_ReturnsNull_NotAttempted()
    {
        var report = new ConversionReport();
        var ctx = SourceEnrichmentContext.TryCreate(
            new SourceConfig { Path = "x.mbox", Type = "mbox", MsfPath = null }, Opts(), report);
        Assert.Null(ctx);
        Assert.Equal(0, report.EnrichmentSummary.SourcesAttempted);
    }

    [Fact]
    public void TryCreate_MissingMsf_Degrades_WithWarning()
    {
        var report = new ConversionReport();
        var ctx = SourceEnrichmentContext.TryCreate(
            new SourceConfig { Path = "x.mbox", Type = "mbox", MsfPath = "does-not-exist.msf" }, Opts(), report);
        Assert.Null(ctx);
        Assert.Equal(1, report.EnrichmentSummary.SourcesAttempted);
        Assert.Equal(1, report.EnrichmentSummary.SourcesDegraded);
        Assert.Equal(1, report.WarningCount);
    }

    [Fact]
    public void TryCreate_MalformedMsf_Degrades_WithWarning()
    {
        // Genuine Mork-level corruption (an unterminated row) — MorkReader fails loud, so the source
        // degrades and warns. NOTE: this must be malformed Mork, not merely table-less: a valid .msf
        // with no msgs table is an empty folder (see TryCreate_EmptyMsf_NoMsgsTable_Enriched_NoWarning),
        // not a degradation.
        string mbox = Write(Mbox(), ".mbox");
        string msf = Write("[1(^88=1)", ".msf");
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.Null(ctx);
            Assert.Equal(1, report.EnrichmentSummary.SourcesDegraded);
            Assert.Equal(1, report.WarningCount);
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_EmptyMsf_NoMsgsTable_Enriched_NoWarning()
    {
        // A valid .msf with column dicts but no msgs table = an empty Thunderbird folder. It must be
        // treated as "enriched, zero messages" — NOT a degradation and NOT a warning (Bucket B noise).
        string mbox = Write(Mbox(), ".mbox");
        string msf = Write(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(89=message-id) >\n",
            ".msf");
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.NotNull(ctx);
            Assert.Equal(1, report.EnrichmentSummary.SourcesEnriched);
            Assert.Equal(0, report.EnrichmentSummary.SourcesDegraded);
            Assert.Equal(0, report.WarningCount);
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_UnreadableMbox_NoWarning_NotDegraded()
    {
        // .msf is valid, but the mbox does not exist: the pre-pass fails -> no context, NO .msf warning,
        // NOT degraded (the normal parse path will record the source skip elsewhere).
        string msf = Write(MsfText, ".msf");
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = "does-not-exist.mbox", Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.Null(ctx);
            Assert.Equal(1, report.EnrichmentSummary.SourcesAttempted);
            Assert.Equal(0, report.EnrichmentSummary.SourcesDegraded);
            Assert.Equal(0, report.WarningCount);
        }
        finally { File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_Valid_AppliesEnrichment()
    {
        string mbox = Write(Mbox(), ".mbox");
        string msf = Write(MsfText, ".msf");
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.NotNull(ctx);
            Assert.Equal(1, report.EnrichmentSummary.SourcesEnriched);

            var mail = new MailMessage { MessageId = "<a@h>", IsRead = false, IsFlagged = false };
            ctx!.Apply(mail);
            Assert.True(mail.IsRead);    // flags=5 -> read
            Assert.True(mail.IsFlagged); // flags=5 -> marked
            Assert.Equal(1, ctx.Result.Matched);
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_MissingMsf_EmitsWarningEvent()
    {
        var report = new ConversionReport();
        var events = new List<ConversionProgressEvent>();
        var ctx = SourceEnrichmentContext.TryCreate(
            new SourceConfig { Path = "x.mbox", Type = "mbox", MsfPath = "does-not-exist.msf" },
            Opts(), report, events.Add);
        Assert.Null(ctx);
        WarningEvent w = Assert.Single(events.OfType<WarningEvent>());
        Assert.Equal("(.msf)", w.Identifier);
    }

    [Fact]
    public void TryCreate_AlignedMsf_FiltersOrphans_ByIndex()
    {
        // Two physical messages; .msf lists only the SECOND as live (storeToken = its byte offset).
        string m1 = "From a@b\nMessage-ID: <a@h>\nSubject: t\n\nbody\n\n";
        string m2 = "From a@b\nMessage-ID: <a@h>\nSubject: t\n\nbody\n";
        long secondOffset = System.Text.Encoding.ASCII.GetByteCount(m1);
        string mbox = Write(m1 + m2, ".mbox");
        string msf = Write(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(CE=storeToken) >\n" +
            "{1:^80 {(k^96:c)} [1(^88=1)(^CE=" + secondOffset + ")] }", ".msf");
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.NotNull(ctx);
            Assert.True(ctx!.ShouldDropOrphan(0));    // index 0 = dead copy
            Assert.False(ctx.ShouldDropOrphan(1));    // index 1 = live
            Assert.Equal(0, ctx.Result.OrphanedCopiesDropped); // PURE predicate: no mutation (counted in runner)
            Assert.Equal(1, ctx.Result.LiveOffsetFilterEnabledSources); // set on Result in TryCreate
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_MisalignedMsf_DisablesFilter_KeepsAll_NoUserWarning()
    {
        string mbox = Write("From a@b\nMessage-ID: <a@h>\nSubject: t\n\nbody\n", ".mbox");
        string msf = Write(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(CE=storeToken) >\n" +
            "{1:^80 {(k^96:c)} [1(^88=1)(^CE=999999)] }", ".msf"); // offset matches no boundary
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.NotNull(ctx);
            Assert.False(ctx!.ShouldDropOrphan(0));   // disabled -> never drops
            // A disabled filter is an internal optimisation declining to run, not a user-actionable
            // problem: the count is kept for diagnostics, but NO warning is surfaced.
            Assert.Equal(1, ctx.Result.LiveOffsetFilterDisabledSources);
            Assert.Equal(1, report.EnrichmentSummary.SourcesEnriched); // still enriched, NOT degraded
            Assert.Equal(0, report.WarningCount);
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_OneRowWithoutUsableOffset_DisablesFilter_KeepsAll_NoUserWarning()
    {
        // Two physical messages; .msf has the first row's storeToken numeric but the second row has NO
        // usable offset -> activation condition 3 fails -> keep all, count it, but DON'T warn.
        string m1 = "From a@b\nMessage-ID: <a@h>\nSubject: t\n\nbody\n\n";
        string mbox = Write(m1 + "From c@d\nMessage-ID: <c@h>\nSubject: t\n\nbody\n", ".mbox");
        string msf = Write(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(CE=storeToken) >\n" +
            "{1:^80 {(k^96:c)} [1(^88=1)(^CE=0)] [2(^88=1)] }", ".msf"); // row 2 has no storeToken
        var report = new ConversionReport();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.NotNull(ctx);
            Assert.False(ctx!.ShouldDropOrphan(0));   // disabled -> nothing dropped, even index 0
            Assert.Equal(1, ctx.Result.LiveOffsetFilterDisabledSources);
            Assert.Equal(0, report.WarningCount);
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_DisabledFilter_EmitsNoWarningEvent()
    {
        // The disabled-filter case must not push a WarningEvent into the streaming progress channel
        // (which the CLI/GUI surface to the user) — contrast TryCreate_MissingMsf_EmitsWarningEvent.
        string mbox = Write("From a@b\nMessage-ID: <a@h>\nSubject: t\n\nbody\n", ".mbox");
        string msf = Write(
            "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(CE=storeToken) >\n" +
            "{1:^80 {(k^96:c)} [1(^88=1)(^CE=999999)] }", ".msf");
        var report = new ConversionReport();
        var events = new List<ConversionProgressEvent>();
        try
        {
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report, events.Add);
            Assert.NotNull(ctx);
            Assert.Equal(1, ctx!.Result.LiveOffsetFilterDisabledSources);
            Assert.Empty(events.OfType<WarningEvent>());
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }

    [Fact]
    public void TryCreate_LockedMsf_Degrades()
    {
        // FileShare.None deterministically blocks the open on Windows (the product's target OS); Unix
        // file locking is advisory, so this exclusivity does not hold there — guard rather than delete.
        if (!OperatingSystem.IsWindows()) return;

        // A handle that shares nothing (FileShare.None) makes even the ReadWrite-share open fail.
        string mbox = Write(Mbox(), ".mbox");
        string msf = Write(MsfText, ".msf");
        var report = new ConversionReport();
        try
        {
            using var locker = new FileStream(msf, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var ctx = SourceEnrichmentContext.TryCreate(
                new SourceConfig { Path = mbox, Type = "mbox", MsfPath = msf }, Opts(), report);
            Assert.Null(ctx);
            Assert.Equal(1, report.EnrichmentSummary.SourcesDegraded);
            Assert.Equal(1, report.WarningCount);
        }
        finally { File.Delete(mbox); File.Delete(msf); }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class MsfEnricherTests
{
    private const string MsgsScope = MsfMessageReader.MsgsScope;
    private const string MsgsKind  = MsfMessageReader.MsgsKind;

    private static MorkRow Row(string id, params (string col, string val)[] cells) =>
        new MorkRow(id, cells.ToDictionary(c => c.col, c => c.val, System.StringComparer.Ordinal));

    private static MsfReadResult Msf(params MorkRow[] rows)
    {
        var dict = rows.ToDictionary(r => r.Id, r => r, System.StringComparer.Ordinal);
        var table = new MorkTable("1", MsgsScope, MsgsKind, dict);
        return MsfMessageReader.Read(new MorkDocument(new[] { table }));
    }

    private static MsfEnrichmentOptions Opts(JunkHandlingMode junk = JunkHandlingMode.Off) =>
        new() { TagResolver = new DefaultMsfTagResolver(), JunkHandling = junk };

    [Fact]
    public void UniqueMatch_OverridesFlags_SetsJunk_AddsCategories()
    {
        var mail = new MailMessage { MessageId = "<a@h>", IsRead = false, IsFlagged = false };
        // flags 5 = read+marked; junkscore 80; keywords "$label1 work".
        // Cells are built directly here (no Mork tokenizer), so the literal "$label1" is fine — no $24 escape.
        MsfReadResult msf = Msf(Row("1", ("message-id", "a@h"), ("flags", "5"),
            ("junkscore", "80"), ("keywords", "$label1 work")));

        var result = MsfEnricher.Enrich(new[] { mail }, msf, Opts());

        Assert.Equal(1, result.Matched);
        Assert.True(mail.IsRead);
        Assert.True(mail.IsFlagged);
        Assert.True(mail.IsJunk);
        Assert.Equal(new[] { "Important", "work" }, mail.Categories); // $label1 -> Important, NonJunk filtered, dedup
    }

    [Fact]
    public void DuplicateId_OnMsfSide_SkipsAll_NoEnrichment()
    {
        var mail = new MailMessage { MessageId = "<dup@h>", IsRead = false };
        MsfReadResult msf = Msf(
            Row("1", ("message-id", "dup@h"), ("flags", "1")),
            Row("2", ("message-id", "dup@h"), ("flags", "1")));
        var result = MsfEnricher.Enrich(new[] { mail }, msf, Opts());
        Assert.Equal(0, result.Matched);
        Assert.Equal(1, result.SkippedDuplicateId);
        Assert.False(mail.IsRead); // untouched
    }

    [Fact]
    public void DuplicateId_OnMboxSide_SkipsAll()
    {
        var mail1 = new MailMessage { MessageId = "<dup@h>", IsRead = false };
        var mail2 = new MailMessage { MessageId = "<dup@h>", IsRead = false };
        MsfReadResult msf = Msf(Row("1", ("message-id", "dup@h"), ("flags", "1")));
        var result = MsfEnricher.Enrich(new[] { mail1, mail2 }, msf, Opts());
        Assert.Equal(0, result.Matched);
        Assert.Equal(2, result.SkippedDuplicateId);
        Assert.False(mail1.IsRead); // both untouched
        Assert.False(mail2.IsRead);
    }

    [Fact]
    public void MissingId_IsSkipped_AndCounted()
    {
        var mail = new MailMessage { MessageId = null, IsRead = false };
        MsfReadResult msf = Msf(Row("1", ("message-id", "x@h"), ("flags", "1")));
        var result = MsfEnricher.Enrich(new[] { mail }, msf, Opts());
        Assert.Equal(0, result.Matched);
        Assert.Equal(1, result.SkippedMissingId);
        Assert.False(mail.IsRead);
    }

    [Fact]
    public void NoMsfMatch_LeavesMessageUnchanged()
    {
        var mail = new MailMessage { MessageId = "<nomatch@h>", IsRead = false };
        MsfReadResult msf = Msf(Row("1", ("message-id", "other@h"), ("flags", "1")));
        var result = MsfEnricher.Enrich(new[] { mail }, msf, Opts());
        Assert.Equal(1, result.NoMsfMatch);
        Assert.False(mail.IsRead);
    }

    [Fact]
    public void JunkCategoryMode_AddsJunkCategory_WhenJunk()
    {
        var mail = new MailMessage { MessageId = "<j@h>" };
        MsfReadResult msf = Msf(Row("1", ("message-id", "j@h"), ("junkscore", "90"), ("keywords", "work")));
        MsfEnricher.Enrich(new[] { mail }, msf, Opts(JunkHandlingMode.Category));
        Assert.Contains("Junk", mail.Categories);
        Assert.Contains("work", mail.Categories);
    }

    [Fact]
    public void JunkOffMode_DoesNotAddJunkCategory()
    {
        var mail = new MailMessage { MessageId = "<j2@h>" };
        MsfReadResult msf = Msf(Row("1", ("message-id", "j2@h"), ("junkscore", "90")));
        MsfEnricher.Enrich(new[] { mail }, msf, Opts(JunkHandlingMode.Off));
        Assert.True(mail.IsJunk);
        Assert.DoesNotContain("Junk", mail.Categories);
    }

    [Fact]
    public void ExpungedMatchedRow_IsKept_AndCounted()
    {
        var mail = new MailMessage { MessageId = "<e@h>", IsRead = false };
        MsfReadResult msf = Msf(Row("1", ("message-id", "e@h"), ("flags", "9"))); // 0x9 = read+expunged
        var result = MsfEnricher.Enrich(new[] { mail }, msf, Opts());
        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.ExpungedMatched);
        Assert.True(mail.IsRead); // still enriched, not dropped
    }

    [Fact]
    public void CategoriesMergeWithExisting_DedupePreservingFirst()
    {
        var mail = new MailMessage { MessageId = "<m@h>", Categories = { "work", "keep" } };
        MsfReadResult msf = Msf(Row("1", ("message-id", "m@h"), ("keywords", "work newtag")));
        MsfEnricher.Enrich(new[] { mail }, msf, Opts());
        Assert.Equal(new[] { "work", "keep", "newtag" }, mail.Categories);
    }
}

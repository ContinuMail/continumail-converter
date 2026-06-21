// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class MsfJoinIndexTests
{
    private static MorkRow Row(string id, params (string col, string val)[] cells) =>
        new MorkRow(id, cells.ToDictionary(c => c.col, c => c.val, StringComparer.Ordinal));

    private static MsfReadResult Msf(params MorkRow[] rows)
    {
        var dict = rows.ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);
        var table = new MorkTable("1", MsfMessageReader.MsgsScope, MsfMessageReader.MsgsKind, dict);
        return MsfMessageReader.Read(new MorkDocument(new[] { table }));
    }

    [Fact]
    public void Build_UniqueId_TryGetUniqueTrue()
    {
        MsfJoinIndex idx = MsfJoinIndex.Build(Msf(Row("1", ("message-id", "a@h"), ("flags", "1"))));
        Assert.True(idx.TryGetUnique("<a@h>", out MsfMessage row));
        Assert.True(row.IsRead);
        Assert.False(idx.IsDuplicateMsfId("<a@h>"));
    }

    [Fact]
    public void Build_DuplicateId_RemovedAndMarked()
    {
        MsfJoinIndex idx = MsfJoinIndex.Build(Msf(
            Row("1", ("message-id", "d@h"), ("flags", "1")),
            Row("2", ("message-id", "d@h"), ("flags", "1"))));
        Assert.False(idx.TryGetUnique("<d@h>", out _));
        Assert.True(idx.IsDuplicateMsfId("<d@h>"));
    }

    [Fact]
    public void Build_BlankMessageId_NotIndexed()
    {
        MsfJoinIndex idx = MsfJoinIndex.Build(Msf(Row("1", ("flags", "1")))); // no message-id cell
        Assert.False(idx.TryGetUnique("<x@h>", out _));
    }

    [Fact]
    public void MboxDuplicateIdSet_FromMessages_FlagsRepeats()
    {
        var set = MboxDuplicateIdSet.FromMessages(new List<MailMessage>
        {
            new() { MessageId = "<a@h>" }, new() { MessageId = "<a@h>" }, new() { MessageId = "<b@h>" },
        });
        Assert.True(set.Contains("<a@h>"));
        Assert.False(set.Contains("<b@h>"));
    }

    [Fact]
    public void MboxDuplicateIdSet_FromCounts_FlagsGreaterThanOne()
    {
        var set = MboxDuplicateIdSet.FromCounts(new Dictionary<string, int>(StringComparer.Ordinal)
            { ["<a@h>"] = 2, ["<b@h>"] = 1 });
        Assert.True(set.Contains("<a@h>"));
        Assert.False(set.Contains("<b@h>"));
    }
}

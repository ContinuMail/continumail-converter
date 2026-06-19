// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class MsfMessageReaderTests
{
    // Reuse the production constants (internal, visible via InternalsVisibleTo) — no duplicated literals.
    private const string MsgsScope = MsfMessageReader.MsgsScope;
    private const string MsgsKind  = MsfMessageReader.MsgsKind;

    // Build a MorkRow directly from (column, value) pairs — bypasses Mork syntax/escaping entirely.
    private static MorkRow Row(string id, params (string col, string val)[] cells) =>
        new MorkRow(id, cells.ToDictionary(c => c.col, c => c.val, StringComparer.Ordinal));

    // A MorkDocument whose single table is the msgs table containing the given rows.
    private static MorkDocument MsgsDoc(params MorkRow[] rows)
    {
        var dict = rows.ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);
        var table = new MorkTable("1", MsgsScope, MsgsKind, dict);
        return new MorkDocument(new[] { table });
    }

    [Fact]
    public void Read_NullDocument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MsfMessageReader.Read(null!));
    }

    [Fact]
    public void Read_NoMsgsTable_ThrowsMorkFormatException()
    {
        var doc = new MorkDocument(Array.Empty<MorkTable>());
        var ex = Assert.Throws<MorkFormatException>(() => MsfMessageReader.Read(doc));
        Assert.Contains("found 0", ex.Message);
    }

    [Fact]
    public void Read_TwoDistinctMsgsTables_ThrowsMorkFormatException()
    {
        // Two DISTINCT table ids with the same scope/kind — genuine ambiguity (not a restate/merge).
        var t1 = new MorkTable("1", MsgsScope, MsgsKind,
            new Dictionary<string, MorkRow> { ["A"] = Row("A") });
        var t2 = new MorkTable("2", MsgsScope, MsgsKind,
            new Dictionary<string, MorkRow> { ["B"] = Row("B") });
        var doc = new MorkDocument(new[] { t1, t2 });
        var ex = Assert.Throws<MorkFormatException>(() => MsfMessageReader.Read(doc));
        Assert.Contains("found 2", ex.Message);
    }

    [Fact]
    public void Read_Row_DefaultsWhenColumnsAbsent()
    {
        MsfReadResult result = MsfMessageReader.Read(MsgsDoc(Row("D9D1")));
        MsfMessage m = Assert.Single(result.Messages);
        Assert.Equal("D9D1", m.RowId);
        Assert.Equal(MsfMessageFlags.None, m.RawFlags);
        Assert.False(m.IsRead);
        Assert.Null(m.JunkScore);
        Assert.False(m.IsJunk);
        Assert.Empty(m.Keywords);
        Assert.Equal(0, m.Label);
        Assert.Null(m.MsgOffset);
        Assert.Null(m.MessageId);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("1",  true,  false, false, false, false)] // read
    [InlineData("0",  false, false, false, false, false)] // unread
    [InlineData("3",  true,  true,  false, false, false)] // read+replied
    [InlineData("5",  true,  false, false, true,  false)] // read+marked
    [InlineData("81", true,  false, false, false, false)] // read+offline
    [InlineData("80", false, false, false, false, false)] // unread+offline
    [InlineData("8",  false, false, false, false, true)]  // expunged
    [InlineData("1000", false, false, true, false, false)] // forwarded
    [InlineData("85", true,  false, false, true,  false)] // read+marked+offline
    [InlineData("91", true,  false, false, false, false)] // read+hasRe+offline
    [InlineData("93", true,  true,  false, false, false)] // read+replied+hasRe+offline
    [InlineData("87", true,  true,  false, true,  false)] // read+replied+marked+offline
    public void Read_Flags_Interpreted(string hex, bool read, bool replied, bool forwarded, bool marked, bool expunged)
    {
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("flags", hex)))).Messages);
        Assert.Equal(read,      m.IsRead);
        Assert.Equal(replied,   m.IsReplied);
        Assert.Equal(forwarded, m.IsForwarded);
        Assert.Equal(marked,    m.IsFlagged);
        Assert.Equal(expunged,  m.IsExpunged);
    }

    [Fact]
    public void Read_Flags_UpperAndLowerHex_ParseSame()
    {
        var lower = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("flags", "ff")))).Messages);
        var upper = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("flags", "FF")))).Messages);
        Assert.Equal(upper.RawFlags, lower.RawFlags);
        Assert.Equal((MsfMessageFlags)0xFFu, lower.RawFlags);
    }

    [Fact]
    public void Read_Flags_UnknownBits_PreservedInRawFlags()
    {
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("flags", "ffffffff")))).Messages);
        Assert.Equal((MsfMessageFlags)0xFFFFFFFFu, m.RawFlags);
    }

    [Fact]
    public void Read_Flags_EmptyOrAbsent_DefaultsToNone_NoDiagnostic()
    {
        var empty = MsfMessageReader.Read(MsgsDoc(Row("1", ("flags", ""))));
        Assert.Equal(MsfMessageFlags.None, Assert.Single(empty.Messages).RawFlags);
        Assert.Empty(empty.Diagnostics);
    }

    [Theory]
    [InlineData("100000000")] // overflow > 0xFFFFFFFF
    [InlineData("zz")]        // non-hex
    public void Read_Flags_Invalid_DefaultsToNone_PlusDiagnostic(string raw)
    {
        MsfReadResult result = MsfMessageReader.Read(MsgsDoc(Row("R1", ("flags", raw))));
        Assert.Equal(MsfMessageFlags.None, Assert.Single(result.Messages).RawFlags);
        MsfDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal("R1", d.RowId);
        Assert.Equal("flags", d.Column);
        Assert.Equal(raw, d.RawValue);
    }
}

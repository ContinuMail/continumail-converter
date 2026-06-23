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
        Assert.Null(m.Priority);
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
        Assert.Equal("not valid hex", d.Reason);
    }

    [Theory]
    [InlineData("0",   0,   false)]
    [InlineData("49",  49,  false)]
    [InlineData("50",  50,  true)]
    [InlineData("100", 100, true)]
    public void Read_JunkScore_ThresholdAt50(string raw, int expected, bool isJunk)
    {
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("junkscore", raw)))).Messages);
        Assert.Equal(expected, m.JunkScore);
        Assert.Equal(isJunk, m.IsJunk);
    }

    [Fact]
    public void Read_JunkScore_EmptyOrAbsent_NullNotJunk_NoDiagnostic()
    {
        var r = MsfMessageReader.Read(MsgsDoc(Row("1", ("junkscore", ""))));
        MsfMessage m = Assert.Single(r.Messages);
        Assert.Null(m.JunkScore);
        Assert.False(m.IsJunk);
        Assert.Empty(r.Diagnostics);
    }

    [Theory]
    [InlineData("high")]          // non-numeric
    [InlineData("99999999999")]   // overflow int
    public void Read_JunkScore_Invalid_NullPlusDiagnostic(string raw)
    {
        MsfReadResult r = MsfMessageReader.Read(MsgsDoc(Row("R1", ("junkscore", raw))));
        Assert.Null(Assert.Single(r.Messages).JunkScore);
        MsfDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal("junkscore", d.Column);
        Assert.Equal(raw, d.RawValue);
    }

    [Fact]
    public void Read_Keywords_SplitDedupeCaseSensitivePreserveOrder()
    {
        MsfMessage m = Assert.Single(
            MsfMessageReader.Read(MsgsDoc(Row("1", ("keywords", "$label1 work $label1 Work")))).Messages);
        Assert.Equal(new[] { "$label1", "work", "Work" }, m.Keywords);
    }

    [Fact]
    public void Read_Keywords_CollapsesEmptyTokensFromExtraSpaces()
    {
        MsfMessage m = Assert.Single(
            MsfMessageReader.Read(MsgsDoc(Row("1", ("keywords", "  a   b ")))).Messages);
        Assert.Equal(new[] { "a", "b" }, m.Keywords);
    }

    [Fact]
    public void Read_Keywords_EmptyOrAbsent_Empty_NoDiagnostic()
    {
        var r = MsfMessageReader.Read(MsgsDoc(Row("1", ("keywords", ""))));
        Assert.Empty(Assert.Single(r.Messages).Keywords);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void Read_Keywords_TabIsPartOfToken_NotDelimiter()
    {
        // ASCII-space-only split BY INTENT: a tab stays inside the token.
        MsfMessage m = Assert.Single(
            MsfMessageReader.Read(MsgsDoc(Row("1", ("keywords", "a\tb c")))).Messages);
        Assert.Equal(new[] { "a\tb", "c" }, m.Keywords);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("7", 7)]
    [InlineData("9", 9)] // out-of-range but valid int — kept verbatim, not clamped
    public void Read_Label_ParsedVerbatim(string raw, int expected)
    {
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("label", raw)))).Messages);
        Assert.Equal(expected, m.Label);
    }

    [Theory]
    [InlineData("red")]           // non-numeric
    [InlineData("99999999999")]   // overflow int
    public void Read_Label_Invalid_ZeroPlusDiagnostic(string raw)
    {
        MsfReadResult r = MsfMessageReader.Read(MsgsDoc(Row("R1", ("label", raw))));
        Assert.Equal(0, Assert.Single(r.Messages).Label);
        MsfDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal("label", d.Column);
        Assert.Equal(raw, d.RawValue);
    }

    [Theory]
    [InlineData("0",     0L)]
    [InlineData("12345", 12345L)]
    public void Read_MsgOffset_Parsed(string raw, long expected)
    {
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("msgOffset", raw)))).Messages);
        Assert.Equal(expected, m.MsgOffset);
    }

    [Theory]
    [InlineData("-1")]                    // negative
    [InlineData("99999999999999999999")]  // overflow long
    [InlineData("xyz")]                    // non-numeric
    public void Read_MsgOffset_Invalid_NullPlusDiagnostic(string raw)
    {
        MsfReadResult r = MsfMessageReader.Read(MsgsDoc(Row("R1", ("msgOffset", raw))));
        Assert.Null(Assert.Single(r.Messages).MsgOffset);
        MsfDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal("msgOffset", d.Column);
        Assert.Equal(raw, d.RawValue);
    }

    [Fact]
    public void Read_MsgOffset_EmptyOrAbsent_Null_NoDiagnostic()
    {
        var r = MsfMessageReader.Read(MsgsDoc(Row("1", ("msgOffset", ""))));
        Assert.Null(Assert.Single(r.Messages).MsgOffset);
        Assert.Empty(r.Diagnostics);
    }

    [Theory]
    [InlineData("0", 0)]   // notSet
    [InlineData("4", 4)]   // normal
    [InlineData("6", 6)]   // highest
    public void Read_Priority_Parsed(string raw, int expected)
    {
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1", ("priority", raw)))).Messages);
        Assert.Equal(expected, m.Priority);
    }

    [Fact]
    public void Read_Priority_EmptyOrAbsent_Null_NoDiagnostic()
    {
        var r = MsfMessageReader.Read(MsgsDoc(Row("1", ("priority", ""))));
        Assert.Null(Assert.Single(r.Messages).Priority);
        Assert.Empty(r.Diagnostics);
    }

    [Theory]
    [InlineData("high")]          // non-numeric
    [InlineData("99999999999")]   // overflow int
    public void Read_Priority_Invalid_NullPlusDiagnostic(string raw)
    {
        MsfReadResult r = MsfMessageReader.Read(MsgsDoc(Row("R1", ("priority", raw))));
        Assert.Null(Assert.Single(r.Messages).Priority);
        MsfDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal("priority", d.Column);
        Assert.Equal(raw, d.RawValue);
    }

    [Fact]
    public void Read_MessageId_Verbatim_NoAngleBracketNormalization()
    {
        // Bare id (no angle brackets) is preserved exactly — SP3 owns normalization for joining.
        MsfMessage m = Assert.Single(
            MsfMessageReader.Read(MsgsDoc(Row("1", ("message-id", "abc@host")))).Messages);
        Assert.Equal("abc@host", m.MessageId);
    }

    [Fact]
    public void Read_MessageId_AngleBracketsPreservedAsStored()
    {
        MsfMessage m = Assert.Single(
            MsfMessageReader.Read(MsgsDoc(Row("1", ("message-id", "<abc@host>")))).Messages);
        Assert.Equal("<abc@host>", m.MessageId);
    }

    [Fact]
    public void Read_MessageId_EmptyString_DefaultsToNull_NoDiagnostic()
    {
        var r = MsfMessageReader.Read(MsgsDoc(Row("1", ("message-id", ""))));
        Assert.Null(Assert.Single(r.Messages).MessageId);
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void Read_MessageId_WhitespacePreservedVerbatim()
    {
        // "verbatim" really means verbatim — no trimming. Locks against a future "helpful" .Trim().
        MsfMessage m = Assert.Single(
            MsfMessageReader.Read(MsgsDoc(Row("1", ("message-id", " abc@host ")))).Messages);
        Assert.Equal(" abc@host ", m.MessageId);
    }

    [Fact]
    public void Read_Diagnostics_MultipleBadCellsInOneRow_FixedColumnOrder()
    {
        // All four diagnostic-producing columns bad in one row.
        MsfReadResult r = MsfMessageReader.Read(MsgsDoc(Row("R1",
            ("flags", "zz"), ("junkscore", "x"), ("label", "y"), ("msgOffset", "-1"))));
        Assert.Equal(
            new[] { "flags", "junkscore", "label", "msgOffset" },
            r.Diagnostics.Select(d => d.Column).ToArray());
        Assert.All(r.Diagnostics, d => Assert.Equal("R1", d.RowId));
    }

    [Fact]
    public void Read_NoReconciliation_ConflictingSignals_AllPreservedIndependently()
    {
        // flags has LabelsMask bits set AND legacy label=3 AND $label1 keyword AND NonJunk AND junkscore=100.
        // SP2 must keep them all, independently — no cross-derivation, no suppression.
        MsfMessage m = Assert.Single(MsfMessageReader.Read(MsgsDoc(Row("1",
            ("flags", "2000000"),                 // a bit inside LabelsMask (0x0E000000)
            ("label", "3"),
            ("keywords", "$label1 NonJunk custom"),
            ("junkscore", "100")))).Messages);

        Assert.Equal((MsfMessageFlags)0x2000000u, m.RawFlags & MsfMessageFlags.LabelsMask);
        Assert.Equal(3, m.Label);
        Assert.Equal(new[] { "$label1", "NonJunk", "custom" }, m.Keywords);
        Assert.Equal(100, m.JunkScore);
        Assert.True(m.IsJunk);
    }

    [Fact]
    public void Read_ParseStringSmoke_InterpretsMsgsTableCells()
    {
        // End-to-end through the real SP1 parser (production consumes MorkReader output).
        // NOTE: in Mork, '$' starts a $XX hex escape, so a LITERAL '$' in a value is encoded $24
        // ('$' == 0x24). Thus on-disk keywords "$label1 work" are written "$24label1 work"; SP2 sees
        // the decoded "$label1 work". (A raw "$label1" in Mork source would throw MorkFormatException
        // "Malformed $XX hex escape" — that is why the direct-construction tests above bypass parsing.)
        const string columnDict =
            "< <(a=c)> " +
            "(80=ns:msg:db:row:scope:msgs:all)" +
            "(96=ns:msg:db:table:kind:msgs)" +
            "(88=flags)(81=message-id)(82=keywords)(83=junkscore)(84=label)(85=msgOffset) >\n";
        const string table =
            "{1:^80 {(k^96:c)} [D9D1(^88=5)(^81=abc@host)(^82=$24label1 work)(^83=50)(^84=3)(^85=123)] }";

        MorkDocument doc = MorkReader.ParseString(columnDict + table);
        MsfReadResult result = MsfMessageReader.Read(doc);
        MsfMessage m = Assert.Single(result.Messages);

        Assert.Equal("D9D1", m.RowId);
        Assert.True(m.IsRead);
        Assert.True(m.IsFlagged);
        Assert.Equal("abc@host", m.MessageId);
        Assert.Equal(new[] { "$label1", "work" }, m.Keywords);
        Assert.True(m.IsJunk);
        Assert.Equal(3, m.Label);
        Assert.Equal(123L, m.MsgOffset);
        Assert.Empty(result.Diagnostics);
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkParseTests
{
    // Column dict: leading <(a=c)> marks this as the column atom space.
    // Defines scope (80), kind (96), and column names (88=flags, 81=subject, A0=hello).
    private const string ColumnDict = "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(81=subject) >\n";

    [Fact]
    public void Parse_TableRow_LiteralAndRefCells()
    {
        // table scope ^80, kind ^96; row 1 with flags=5 (literal) and subject=^A0 (atom ref -> "hello")
        // ^A0 is a value atom ref — needs a separate VALUE dict (no <(a=c)>).
        string src = ColumnDict
            + "< (A0=hello) >\n"
            + "{1:^80 {(k^96:c)} [1(^88=5)(^81^A0)] }";
        MorkDocument doc = MorkReader.ParseString(src);
        Assert.True(doc.TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out MorkTable t));
        MorkRow r = t.Rows["1"];
        Assert.Equal("5", r.Cells["flags"]);
        Assert.Equal("hello", r.Cells["subject"]); // value atom ref resolved from value map
    }

    [Fact]
    public void Parse_DictOnly_NoTables()
    {
        MorkDocument doc = MorkReader.ParseString(ColumnDict);
        Assert.Empty(doc.Tables);
    }

    [Fact]
    public void Parse_UnknownColumnAndScope_Preserved()
    {
        // unknown column atom F0=customThing, unknown table kind -> must NOT throw; preserved as strings
        string src = "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(F0=customThing)(F1=ns:msg:db:table:kind:weird) >\n"
                   + "{1:^80 {(k^F1:c)} [1(^F0=xyz)] }";
        MorkDocument doc = MorkReader.ParseString(src);
        MorkTable t = doc.Tables.Single();
        Assert.Equal("ns:msg:db:table:kind:weird", t.Kind);
        Assert.Equal("xyz", t.Rows["1"].Cells["customThing"]);
    }

    [Fact]
    public void Parse_Charset_Latin1HighByteViaByteStream()
    {
        // charset hint iso-8859-1; subject value is the raw byte 0xE6 ('æ'). MUST use the byte path.
        byte[] src = BuildLatin1Fixture(); // dict declares (f=iso-8859-1); row subject cell value = single 0xE6 byte
        using var ms = new MemoryStream(src);
        MorkDocument doc = MorkReader.Parse(ms);
        Assert.Equal("æ", doc.Tables.Single().Rows["1"].Cells["subject"]);
    }

    [Fact]
    public void Parse_EmptyCell_StoresEmptyString()
    {
        // (^88=5) → flags="5"; (^81=) → subject="" (cell present, value empty literal)
        MorkDocument doc = MorkReader.ParseString(ColumnDict + "{1:^80 {(k^96:c)} [1(^88=5)(^81=)] }");
        Assert.True(doc.TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out MorkTable t));
        MorkRow r = t.Rows["1"];
        Assert.Equal("5", r.Cells["flags"]);
        Assert.Equal("", r.Cells["subject"]);
    }

    [Fact]
    public void Parse_MalformedStructure_Throws()
    {
        Assert.Throws<MorkFormatException>(() => MorkReader.ParseString("< (80=x) " /* unterminated dict */));
    }

    [Fact]
    public void Parse_ColumnVsValueAtomSpaces_SeparateIds()
    {
        // Regression: column dict defines 80=ns:msg:db:row:scope:msgs:all and 96=ns:msg:db:table:kind:msgs
        // and 88=flags. Then a value dict reuses id 80 with a different value.
        // The table scope ^80 must resolve from the COLUMN map (not the value dict),
        // so Scope must be "ns:msg:db:row:scope:msgs:all" — NOT "somevalue".
        // Similarly, Kind ^96 must resolve to "ns:msg:db:table:kind:msgs".
        const string columnDict = "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags) >\n";
        const string valueDict  = "< (80=somevalue) >\n"; // reuses id 80 — must NOT clobber column map
        const string table      = "{1:^80 {(k^96:c)} [1(^88=5)]}";
        MorkDocument doc = MorkReader.ParseString(columnDict + valueDict + table);
        Assert.True(doc.TryGetSingleTable(
            "ns:msg:db:row:scope:msgs:all",
            "ns:msg:db:table:kind:msgs",
            out MorkTable t),
            $"Expected msgs table; actual tables: [{string.Join(", ", doc.Tables.Select(x => $"scope={x.Scope} kind={x.Kind}"))}]");
        Assert.Equal("ns:msg:db:row:scope:msgs:all", t.Scope);
        Assert.Equal("ns:msg:db:table:kind:msgs",   t.Kind);
        Assert.Equal("5", t.Rows["1"].Cells["flags"]);
    }

    [Fact]
    public void Parse_ColumnDict_MarkerPrecededByCharsetCell_StillRecognisedAsColumnDict()
    {
        // Regression for I-1: a column dict where the charset cell (f=iso-8859-1)
        // appears BEFORE the <(a=c)> marker. The dict must still be classified as a
        // column dict so that scope/kind/column atom defs are placed in the column map.
        // Format: < (f=iso-8859-1) <(a=c)> (80=…scope…)(96=…kind…)(88=flags) >
        // followed by a table and a row that uses column refs ^80, ^96, ^88.
        const string src =
            "< (f=iso-8859-1) <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags) >\n"
            + "{1:^80 {(k^96:c)} [1(^88=5)]}";
        MorkDocument doc = MorkReader.ParseString(src);
        Assert.True(
            doc.TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out MorkTable t),
            $"Column dict not recognised — actual tables: [{string.Join(", ", doc.Tables.Select(x => $"scope={x.Scope} kind={x.Kind}"))}]");
        Assert.Equal("ns:msg:db:row:scope:msgs:all", t.Scope);
        Assert.Equal("5", t.Rows["1"].Cells["flags"]);
    }

    // Builds: "< <(a=c)> (f=iso-8859-1)(80=ns:...:msgs:all)(96=ns:...:kind:msgs)(81=subject) >{1:^80 {(k^96:c)} [1(^81=<0xE6>)]}"
    // with the subject value being the single raw byte 0xE6 (NOT $-escaped) to exercise the byte path.
    private static byte[] BuildLatin1Fixture()
    {
        string head = "< <(a=c)> (f=iso-8859-1)(80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(81=subject) >\n{1:^80 {(k^96:c)} [1(^81=";
        string tail = ")] }";
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(head));
        bytes.Add(0xE6);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(tail));
        return bytes.ToArray();
    }
}

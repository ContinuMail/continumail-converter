// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using System.Text;
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkTokenizerTests
{
    private static MorkToken[] Toks(string s) => new MorkTokenizer(Encoding.ASCII.GetBytes(s)).Tokenize().ToArray();

    [Fact]
    public void Tokenize_HeaderCommentAndWhitespace_Ignored()
    {
        // A // line comment and surrounding whitespace/newlines produce no tokens.
        MorkToken[] t = Toks("// <!-- <mdb:mork:z v=\"1.4\"/> -->\n   \r\n");
        Assert.Empty(t);
    }

    [Fact]
    public void Tokenize_DictWithAtom_EmitsDelimitersAndText()
    {
        // < (80=flags) >  -> DictOpen, ParenOpen, Text 80, Equals, Text flags, ParenClose, DictClose
        MorkToken[] t = Toks("< (80=flags) >");
        Assert.Equal(MorkTokenKind.DictOpen, t.First().Kind);
        Assert.Equal(MorkTokenKind.DictClose, t.Last().Kind);
        Assert.Contains(t, x => x.Kind == MorkTokenKind.Text && Encoding.ASCII.GetString(x.Bytes) == "flags");
        Assert.Contains(t, x => x.Kind == MorkTokenKind.Equals);
    }

    [Fact]
    public void Tokenize_RowWithLiteralAndRefCells_EmitsGenericTokens()
    {
        // [1(^88=5)(^81^A0)] -> BracketOpen, Text 1, ParenOpen, AtomRef 88, Equals, Text 5, ParenClose,
        //                       ParenOpen, AtomRef 81, AtomRef A0, ParenClose, BracketClose
        MorkToken[] t = Toks("[1(^88=5)(^81^A0)]");
        Assert.Equal(MorkTokenKind.BracketOpen, t[0].Kind);
        Assert.Equal(MorkTokenKind.BracketClose, t[^1].Kind);
        Assert.Contains(t, x => x.Kind == MorkTokenKind.AtomRef && Encoding.ASCII.GetString(x.Bytes) == "88");
        Assert.Contains(t, x => x.Kind == MorkTokenKind.AtomRef && Encoding.ASCII.GetString(x.Bytes) == "A0");
        Assert.Contains(t, x => x.Kind == MorkTokenKind.Text && Encoding.ASCII.GetString(x.Bytes) == "5");
    }

    [Fact]
    public void Tokenize_RowCut_EmitsCutToken()
    {
        MorkToken[] t = Toks("[-1(^88=1)]");
        Assert.Contains(t, x => x.Kind == MorkTokenKind.Cut);
    }

    [Fact]
    public void Tokenize_TransactionGroupMarkers_Emitted()
    {
        // @$${1{@ ... @$$}1}@  -> GroupStart ... GroupCommit  (exact bytes per Task 0)
        MorkToken[] t = Toks("@$${1{@ [1(^88=1)] @$$}1}@");
        Assert.Contains(t, x => x.Kind == MorkTokenKind.GroupStart);
        Assert.Contains(t, x => x.Kind == MorkTokenKind.GroupCommit);
    }

    [Fact]
    public void Tokenize_DollarHexEscape_DecodesToByte()
    {
        // $E6 in a value contributes byte 0xE6
        MorkToken[] t = Toks("(80=$E6)");
        MorkToken txt = t.Last(x => x.Kind == MorkTokenKind.Text);
        Assert.Equal(new byte[] { 0xE6 }, txt.Bytes);
    }

    [Fact]
    public void Tokenize_BackslashEscape_LiteralChar()
    {
        // \) is a literal ')' inside a value
        MorkToken[] t = Toks(@"(80=a\)b)");
        MorkToken txt = t.Last(x => x.Kind == MorkTokenKind.Text);
        Assert.Equal(Encoding.ASCII.GetBytes("a)b"), txt.Bytes);
    }

    [Fact]
    public void Tokenize_UnterminatedRow_Throws()
    {
        Assert.Throws<MorkFormatException>(() => Toks("[1(^88=1)"));
    }

    [Fact]
    public void Tokenize_ValueWithSpace_PreservesSpace()
    {
        // (80=hello world) — the space between "hello" and "world" is literal value content.
        // Must produce exactly one Text token containing the full string "hello world",
        // not two separate Text tokens with the space discarded.
        MorkToken[] t = Toks("(80=hello world)");
        MorkToken[] valueTokens = t.Where(x => x.Kind == MorkTokenKind.Text && Encoding.ASCII.GetString(x.Bytes) == "hello world").ToArray();
        Assert.Single(valueTokens);
    }

    [Fact]
    public void Tokenize_ValueWithColon_ColonIsLiteralNotToken()
    {
        // (80=Re: hi) — the ':' inside the value is literal, NOT a Colon structural token.
        MorkToken[] t = Toks("(80=Re: hi)");
        // No standalone Colon token should exist
        Assert.DoesNotContain(t, x => x.Kind == MorkTokenKind.Colon);
        // The entire value should be in one Text token containing ':'
        MorkToken txt = Assert.Single(t, x => x.Kind == MorkTokenKind.Text && Encoding.ASCII.GetString(x.Bytes).Contains(":"));
        Assert.Equal("Re: hi", Encoding.ASCII.GetString(txt.Bytes));
    }

    [Fact]
    public void Tokenize_EmptyValue_NoTextTokenBetweenEqualsAndParenClose()
    {
        // (^81=) — empty value after '=': must emit ParenOpen, AtomRef, Equals, ParenClose
        // with NO Text token between Equals and ParenClose.
        MorkToken[] t = Toks("(^81=)");
        Assert.Equal(MorkTokenKind.ParenOpen,  t[0].Kind);
        Assert.Equal(MorkTokenKind.AtomRef,    t[1].Kind);
        Assert.Equal("81", Encoding.ASCII.GetString(t[1].Bytes));
        Assert.Equal(MorkTokenKind.Equals,     t[2].Kind);
        Assert.Equal(MorkTokenKind.ParenClose, t[3].Kind);
        Assert.Equal(4, t.Length);
    }
}

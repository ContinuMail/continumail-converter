// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class PrefsTagReaderTests
{
    [Fact]
    public void ParsesBuiltinAndCustomTagNames()
    {
        string text =
            "user_pref(\"mailnews.tags.$label1.tag\", \"Critique\");\n" +
            "user_pref(\"mailnews.tags.custom.tag\", \"Client X\");\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal("Critique", map["$label1"]);
        Assert.Equal("Client X", map["custom"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void IgnoresColorAndUnrelatedPrefs()
    {
        string text =
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n" +
            "user_pref(\"mail.server.server1.check_new_mail\", true);\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal(new[] { "$label1" }, new List<string>(map.Keys));
    }

    [Fact]
    public void SkipsCommentAndMalformedLines()
    {
        string text =
            "// user_pref(\"mailnews.tags.$label1.tag\", \"Commented\");\n" +
            "user_pref(\"mailnews.tags.$label2.tag\",\n" +   // truncated/malformed line
            "user_pref(\"mailnews.tags.$label3.tag\", \"Personal\");\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.False(map.ContainsKey("$label1"));
        Assert.False(map.ContainsKey("$label2"));
        Assert.Equal("Personal", map["$label3"]);
    }

    [Fact]
    public void ToleratesWhitespace()
    {
        string text = "   user_pref( \"mailnews.tags.$label1.tag\" ,  \"Important\" ) ;  \n";
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal("Important", map["$label1"]);
    }

    [Fact]
    public void UnescapesStringLiterals()
    {
        string text =
            "user_pref(\"mailnews.tags.a.tag\", \"q\\\"q\");\n" +        // q"q
            "user_pref(\"mailnews.tags.b.tag\", \"x\\\\y\");\n" +       // x\y
            "user_pref(\"mailnews.tags.c.tag\", \"u\\u00e9\");\n" +     // ué
            "user_pref(\"mailnews.tags.d.tag\", \"g\\nhi\\rj\\tk\");\n"; // g<LF>hi<CR>j<TAB>k
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal("q\"q", map["a"]);
        Assert.Equal("x\\y", map["b"]);
        Assert.Equal("ué", map["c"]);
        Assert.Equal("g\nhi\rj\tk", map["d"]);
    }

    [Fact]
    public void DoesNotNormalizeKey()
    {
        string text = "user_pref(\"mailnews.tags.Custom-Key_1.tag\", \"Name\");\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal("Name", map["Custom-Key_1"]); // key kept verbatim, no lowercase/trim/normalize
    }

    [Fact]
    public void SkipsLineWithUnparseableEscape()
    {
        string text =
            "user_pref(\"mailnews.tags.bad.tag\", \"x\\q\");\n" +    // \q is not a known escape
            "user_pref(\"mailnews.tags.ok.tag\", \"Fine\");\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.False(map.ContainsKey("bad"));
        Assert.Equal("Fine", map["ok"]);
    }

    [Fact]
    public void SkipsEmptyDisplayName()
    {
        string text = "user_pref(\"mailnews.tags.foo.tag\", \"\");\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.False(map.ContainsKey("foo"));
    }

    [Fact]
    public void LaterDuplicateWins_IncludingEarlierMalformed()
    {
        string text =
            "user_pref(\"mailnews.tags.$label1.tag\", \"First\");\n" +
            "user_pref(\"mailnews.tags.$label1.tag\", \"Second\");\n" +
            "user_pref(\"mailnews.tags.dup.tag\", \"x\\q\");\n" +   // malformed -> skipped
            "user_pref(\"mailnews.tags.dup.tag\", \"Valid\");\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal("Second", map["$label1"]);
        Assert.Equal("Valid", map["dup"]);
    }

    [Fact]
    public void Read_MissingFile_ReturnsEmptyMap()
    {
        var map = PrefsTagReader.Read(Path.Combine(Path.GetTempPath(), "no-such-" + System.Guid.NewGuid() + ".js"));
        Assert.Empty(map);
    }

    [Fact]
    public void Read_PresentFile_Parses()
    {
        string p = Path.Combine(Path.GetTempPath(), "prefs-" + System.Guid.NewGuid() + ".js");
        File.WriteAllText(p, "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n");
        try { Assert.Equal("Important", PrefsTagReader.Read(p)["$label1"]); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void HandlesCrlfLineEndings()
    {
        string text =
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\r\n" +
            "user_pref(\"mailnews.tags.custom.tag\", \"Client X\");\r\n";
        var map = PrefsTagReader.ParseText(text);
        Assert.Equal("Important", map["$label1"]);
        Assert.Equal("Client X", map["custom"]);
    }
}

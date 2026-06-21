// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class PrefsTagReaderColorTests
{
    [Fact]
    public void ParsesColorPrefs_IgnoringTagAndUnrelated()
    {
        string text =
            "user_pref(\"mailnews.tags.$label1.color\", \"#FF0000\");\n" +
            "user_pref(\"mailnews.tags.custom.color\", \"#12ab9C\");\n" +
            "user_pref(\"mailnews.tags.$label1.tag\", \"Important\");\n" +    // .tag ignored
            "user_pref(\"mail.server.server1.color\", \"#000000\");\n";       // unrelated ignored
        var map = PrefsTagReader.ParseColors(text);
        Assert.Equal("#FF0000", map["$label1"]);
        Assert.Equal("#12ab9C", map["custom"]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void SkipsMalformedHex()
    {
        string text =
            "user_pref(\"mailnews.tags.a.color\", \"red\");\n" +       // not hex
            "user_pref(\"mailnews.tags.b.color\", \"#F00\");\n" +      // 3-digit, not 6
            "user_pref(\"mailnews.tags.c.color\", \"#00FF00\");\n";
        var map = PrefsTagReader.ParseColors(text);
        Assert.False(map.ContainsKey("a"));
        Assert.False(map.ContainsKey("b"));
        Assert.Equal("#00FF00", map["c"]);
    }

    [Fact]
    public void MissingPrefs_EmptyMap()
        => Assert.Empty(PrefsTagReader.ParseColors(""));
}

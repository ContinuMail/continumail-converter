// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.OutlookCategories;
using Xunit;

public class CategoryColorHasherTests
{
    [Theory]
    [InlineData("Meeting", "#FFFF66")]    // verified against Thunderbird UI (yellow)
    [InlineData("Suppliers", "#000099")]  // verified against Thunderbird UI (dark blue)
    [InlineData("", "#FF6600")]           // empty -> " " (code 32) -> idx 32
    public void HashColor_matches_thunderbird(string name, string expected)
    {
        Assert.Equal(expected, CategoryColorHasher.HashColor(name));
    }

    [Fact]
    public void HashColor_nonBMP_sums_only_high_surrogate_per_codepoint()
    {
        // "🎂" (U+1F382) -> one code point -> its first UTF-16 unit is the high surrogate 0xD83C (55356).
        // 55356 % 70 == 56 -> palette[56] == "#336666". (Summing BOTH surrogates would give a different index.)
        Assert.Equal("#336666", CategoryColorHasher.HashColor("🎂"));
    }

    [Theory]
    [InlineData("Meeting", "meeting")]
    [InlineData("Follow up", "follow_up")]
    [InlineData("Grab E-Receipt", "grab_e-ux2d-receipt")]
    [InlineData("Café", "caf-uxe9-")]
    public void FormatStringForCSSRule_matches_thunderbird(string name, string expected)
    {
        Assert.Equal(expected, CategoryColorHasher.FormatStringForCSSRule(name));
    }
}

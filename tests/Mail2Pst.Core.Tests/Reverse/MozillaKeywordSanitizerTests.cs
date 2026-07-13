// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MozillaKeywordSanitizerTests
{
    [Theory]
    [InlineData("Work", true)]
    [InlineData("work_2", true)]
    [InlineData("_x", true)]
    [InlineData("Follow Up", false)]   // space
    [InlineData("a-b", false)]         // hyphen
    [InlineData("café", false)]        // non-ASCII letter
    [InlineData("", false)]
    public void IsSafeKey_Cases(string key, bool expected)
        => Assert.Equal(expected, MozillaKeywordSanitizer.IsSafeKey(key));

    [Theory]
    [InlineData("Work", "Work")]            // safe key preserved verbatim (original case kept)
    [InlineData("work_2", "work_2")]
    [InlineData("Follow Up", "follow_up")]  // lowercase + whitespace -> underscore
    [InlineData("Follow  Up", "follow_up")] // whitespace run collapses to one underscore
    [InlineData("  Padded  ", "padded")]    // leading/trailing separators trimmed
    [InlineData("Follow-Up", "follow_up")]  // hyphen -> single underscore separator
    [InlineData("Q1/Q2", "q1_q2")]          // slash -> separator
    [InlineData("example.com", "example_com")] // dot -> separator
    [InlineData("Café", "caf")]             // trailing non-ASCII -> separator, trimmed
    [InlineData("a - b", "a_b")]            // space-hyphen-space run -> single underscore
    [InlineData("a_b!", "a_b")]             // unsafe (has '!'): '_' + trailing '!' both collapse/trim
    [InlineData("a__b!", "a_b")]            // run of '_' collapses to one; trailing '!' trimmed
    public void Sanitize_ProducesKey(string input, string expected)
        => Assert.Equal(expected, MozillaKeywordSanitizer.Sanitize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Sanitize_NothingUsable_ReturnsNull(string input)
        => Assert.Null(MozillaKeywordSanitizer.Sanitize(input));
}

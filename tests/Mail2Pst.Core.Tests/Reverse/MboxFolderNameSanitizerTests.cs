// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MboxFolderNameSanitizerTests
{
    [Theory]
    [InlineData("Inbox", "Inbox")]                 // already safe, preserved
    [InlineData("Team/Alpha", "Team Alpha")]       // forward Sanitize maps '/' -> space
    [InlineData("In:box*?", "In_box__")]           // Windows-illegal filename chars -> '_'
    [InlineData("a\"b<c>d|e", "a_b_c_d_e")]         // more illegal chars -> '_'
    [InlineData("  spaced  ", "spaced")]           // leading/trailing whitespace trimmed
    [InlineData("dotted.", "dotted")]              // trailing dot trimmed (Windows)
    [InlineData("CON", "Folder")]                  // reserved device name -> fallback
    [InlineData("", "Folder")]                     // empty -> fallback
    [InlineData("   ", "Folder")]                  // whitespace-only -> fallback
    public void ToFileName_Cases(string input, string expected)
        => Assert.Equal(expected, MboxFolderNameSanitizer.ToFileName(input));

    [Fact]
    public void ToFileName_Null_ReturnsFallback()
        => Assert.Equal("Folder", MboxFolderNameSanitizer.ToFileName(null));
}

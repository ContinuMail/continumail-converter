// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

public class FolderNameValidatorTests
{
    // NOTE: keep this table identical (case-for-case) to the parity table in
    // desktop/src/lib/options.test.ts so the engine and GUI validators can't drift.
    [Theory]
    [InlineData("Imported Mail")]
    [InlineData("Work")]
    [InlineData("2024 Archive")]
    [InlineData("a.b")]
    [InlineData("Folder.name.with.dots")]
    [InlineData("café")]
    [InlineData(".hidden")]   // leading dot: a PST folder display name, not a filesystem name
    [InlineData("trailing.")] // trailing dot: MAPI-internal, never written to disk
    public void Validate_AcceptsValidNames(string name)
    {
        FolderNameValidator.Validate(name); // must not throw
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("tab\there")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("COM1")]
    [InlineData("LPT9.log")]
    public void Validate_RejectsUnsafeNames(string name)
    {
        Assert.Throws<ConfigValidationException>(() => FolderNameValidator.Validate(name));
    }

    [Fact]
    public void Validate_RejectsNull()
    {
        Assert.Throws<ConfigValidationException>(() => FolderNameValidator.Validate(null));
    }

    [Fact]
    public void ValidatePath_SingleSegment_IsValid()
        => FolderNameValidator.ValidatePath(new[] { "Inbox" }); // does not throw

    [Fact]
    public void ValidatePath_NestedSegments_IsValid()
        => FolderNameValidator.ValidatePath(new[] { "Accounts", "Work", "Sent" }); // does not throw

    [Fact]
    public void ValidatePath_EmptyList_Throws()
        => Assert.Throws<ConfigValidationException>(() => FolderNameValidator.ValidatePath(Array.Empty<string>()));

    [Fact]
    public void ValidatePath_Null_Throws()
        => Assert.Throws<ConfigValidationException>(() => FolderNameValidator.ValidatePath(null));

    [Fact]
    public void ValidatePath_BlankSegment_Throws()
        => Assert.Throws<ConfigValidationException>(() => FolderNameValidator.ValidatePath(new[] { "A", "   " }));

    [Fact]
    public void ValidatePath_PaddedSegment_Throws() // pinned: reject, do NOT trim
        => Assert.Throws<ConfigValidationException>(() => FolderNameValidator.ValidatePath(new[] { " A " }));

    [Fact]
    public void ValidatePath_SeparatorInSegment_Throws()
        => Assert.Throws<ConfigValidationException>(() => FolderNameValidator.ValidatePath(new[] { "A/B" }));

    [Fact]
    public void ValidatePath_Depth32_IsValid()
        => FolderNameValidator.ValidatePath(Enumerable.Range(0, 32).Select(i => $"f{i}").ToArray());

    [Fact]
    public void ValidatePath_Depth33_Throws()
        => Assert.Throws<ConfigValidationException>(
            () => FolderNameValidator.ValidatePath(Enumerable.Range(0, 33).Select(i => $"f{i}").ToArray()));
}

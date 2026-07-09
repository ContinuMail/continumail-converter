// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Mail2Pst.Core.Writing;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class MailCategoryComposerTests
{
    [Fact]
    public void Compose_StarredNoTags_YieldsStarOnly()
    {
        Assert.Equal(new[] { "Star" }, MailCategoryComposer.Compose(Array.Empty<string>(), isFlagged: true));
    }

    [Fact]
    public void Compose_StarredWithTags_AppendsStarKeepingTags()
    {
        Assert.Equal(
            new[] { "Work", "Star" },
            MailCategoryComposer.Compose(new[] { "Work" }, isFlagged: true));
    }

    [Fact]
    public void Compose_NotStarredWithTags_LeavesTagsUnchanged_NoStar()
    {
        Assert.Equal(
            new[] { "Work", "Receipts" },
            MailCategoryComposer.Compose(new[] { "Work", "Receipts" }, isFlagged: false));
    }

    [Fact]
    public void Compose_NotStarredNoTags_Empty()
    {
        Assert.Empty(MailCategoryComposer.Compose(Array.Empty<string>(), isFlagged: false));
    }

    [Fact]
    public void Compose_StarredWithExistingStarTag_DoesNotDuplicate_CaseInsensitive()
    {
        // A message already carrying a tag literally named "star" must not gain a second "Star".
        Assert.Equal(
            new[] { "star" },
            MailCategoryComposer.Compose(new[] { "star" }, isFlagged: true));
    }

    [Fact]
    public void Compose_DoesNotMutateInput()
    {
        var input = new List<string> { "Work" };
        MailCategoryComposer.Compose(input, isFlagged: true);
        Assert.Equal(new[] { "Work" }, input); // unchanged
    }
}

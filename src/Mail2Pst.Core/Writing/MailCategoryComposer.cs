// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Computes the Outlook category list written to a mail message: the message's Thunderbird tags,
/// plus the synthetic <see cref="StarCategory"/> ("Star") when the message is flagged/starred
/// (Thunderbird "Marked"). A star is surfaced as a category rather than an Outlook follow-up flag,
/// which would otherwise place every starred message into Outlook's To-Do list. "Star" is
/// <em>appended</em> to the existing tags (never replaces them) and de-duplicated case-insensitively.
/// </summary>
public static class MailCategoryComposer
{
    /// <summary>
    /// Returns <paramref name="categories"/> with "Star" appended when <paramref name="isFlagged"/>
    /// is true and no existing category already equals "Star" (OrdinalIgnoreCase). The input order is
    /// preserved; "Star" (when added) goes last. A new list is always returned; the input is not mutated.
    /// </summary>
    public static List<string> Compose(IReadOnlyList<string> categories, bool isFlagged)
    {
        var result = new List<string>(categories ?? (IReadOnlyList<string>)Array.Empty<string>());
        if (isFlagged &&
            !result.Exists(c => string.Equals(c, StarCategory.Name, StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(StarCategory.Name);
        }
        return result;
    }
}

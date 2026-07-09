// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// The synthetic Outlook category that represents a Thunderbird "Marked" / Gmail "Starred" message.
/// Outlook has no "star" concept; writing an Outlook follow-up flag (the only other marker) would
/// force every starred message into Outlook's To-Do list, so a starred message is surfaced as this
/// yellow category instead — added alongside any real Thunderbird tags, never replacing them.
/// </summary>
public static class StarCategory
{
    /// <summary>The category name assigned to a starred message and baked into the master list.</summary>
    public const string Name = "Star";

    /// <summary>OlCategoryColor value 4 = Yellow (see <see cref="OlCategoryColorMap"/>).</summary>
    public const int OutlookColor = 4;
}

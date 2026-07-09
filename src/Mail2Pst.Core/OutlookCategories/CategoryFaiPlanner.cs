// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core.Msf;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Computes the Outlook colour plan (mail tags from prefs.js + calendar/task category colours) and the
/// master-list XML bytes to bake as the CategoryList FAI. Single source of truth shared by the converter's
/// per-store bake and the CLI's <c>done</c>-event colour summary. Pure except for reading the profile's
/// prefs.js; Outlook-free.
/// </summary>
public static class CategoryFaiPlanner
{
    /// <summary>The colour candidate plan (would-add / skipped-*) for the profile + category names.
    /// Mail-tag colours come from prefs.js (when present); calendar/task category names are ALWAYS
    /// resolved to a colour (a prefs override if present, else the Thunderbird hash colour), so they
    /// bake even with no profile. Returns empty only when there is genuinely nothing to colour — no
    /// prefs.js AND no category names.</summary>
    public static IReadOnlyList<CategoryCandidate> BuildPlan(string? profilePath, IReadOnlyList<string> categoryNames)
    {
        ArgumentNullException.ThrowIfNull(categoryNames);

        // Read prefs.js if the profile has one; otherwise proceed with empty mail-tag inputs so
        // calendar/task category names still resolve to hash colours below.
        string? content = TryReadPrefs(profilePath);
        bool hasPrefs = content is not null;

        // Nothing real to colour: no mail-tag source and no calendar/task categories.
        if (!hasPrefs && categoryNames.Count == 0) return Array.Empty<CategoryCandidate>();

        IReadOnlyDictionary<string, string> tagNames = hasPrefs
            ? PrefsTagReader.ParseText(content!) : Empty;
        IReadOnlyDictionary<string, string> tagColors = hasPrefs
            ? PrefsTagReader.ParseColors(content!) : Empty;
        IReadOnlyDictionary<string, string> overrides = hasPrefs
            ? CalendarCategoryOverrideReader.ParseText(content!) : Empty;

        // Always resolve calendar/task names — the resolver hashes a default colour when there is no
        // override, so categories are coloured even without a profile.
        IReadOnlyDictionary<string, string> calColors =
            CalendarCategoryColorResolver.Resolve(categoryNames, overrides);

        return CategoryColorPlan.Build(tagNames, tagColors, calColors);
    }

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Returns prefs.js contents, or null when there is no profile / no prefs.js / it is unreadable.
    private static string? TryReadPrefs(string? profilePath)
    {
        if (string.IsNullOrEmpty(profilePath)) return null;
        string prefsPath = Path.Combine(profilePath, "prefs.js");
        if (!File.Exists(prefsPath)) return null;
        try { return File.ReadAllText(prefsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The CategoryList master-list XML bytes for the plan's would-add entries, or
    /// <c>null</c> when there is nothing to bake (no profile, or no coloured category).</summary>
    public static byte[]? BuildXmlBytes(string? profilePath, IReadOnlyList<string> categoryNames,
        bool includeStarCategory = false)
    {
        IReadOnlyList<CategoryCandidate> plan = BuildPlan(profilePath, categoryNames);
        var additions = new List<(string Name, int OutlookColor)>();
        foreach (CategoryCandidate c in plan)
            if (c.Action == "would-add" && c.OutlookColor is int color)
                additions.Add((c.Name, color));
        // The synthetic "Star" category (Thunderbird "Marked"/starred → yellow category, see
        // StarCategory) is baked so it renders in colour. Added unless a real tag already claims the
        // name (OrdinalIgnoreCase) — that tag's own colour then wins.
        if (includeStarCategory &&
            !additions.Exists(a => string.Equals(a.Name, StarCategory.Name, StringComparison.OrdinalIgnoreCase)))
            additions.Add((StarCategory.Name, StarCategory.OutlookColor));
        if (additions.Count == 0) return null;
        string xml = CategoryListXml.Append(string.Empty, additions);
        return Encoding.UTF8.GetBytes(xml);
    }
}

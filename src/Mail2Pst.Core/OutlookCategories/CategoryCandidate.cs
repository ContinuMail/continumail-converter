// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.OutlookCategories;

/// <summary>One resolved category for the colour import. Action is a plan action (would-add /
/// skipped-no-colour / skipped-invalid-name) before apply, replaced by the final action after apply
/// (added / skipped-existing). OutlookColor is the OlCategoryColor int (1-25), null when no colour resolved.</summary>
public sealed record CategoryCandidate(string Name, string? Hex, int? OutlookColor, string Action);

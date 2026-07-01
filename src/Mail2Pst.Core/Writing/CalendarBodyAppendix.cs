// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Writing;

/// <summary>Builds ONE deterministic, human-visible body appendix (link-only attachments + preserved
/// relation lines), so there is a single append format. Dedup: a link already present in the existing
/// body text is not re-appended.</summary>
internal static class CalendarBodyAppendix
{
    public static string? Format(
        IReadOnlyList<CalendarAttachment> attachments,
        IReadOnlyList<string> relationLines,
        string? existingText = null)
    {
        var lines = new List<string>();
        foreach (var a in attachments)
            if (a.Kind == CalendarAttachmentKind.LinkOnly && a.PreservedReference is not null
                && (existingText is null || !existingText.Contains(a.PreservedReference)))
                // "reference" not "link": PreservedReference may be a remote URL OR raw un-parseable ATTACH text.
                lines.Add($"[Attachment reference (not embedded): {a.FileName} — {a.PreservedReference}]");
        foreach (var r in relationLines)
            if (existingText is null || !existingText.Contains(r))
                lines.Add($"[Thunderbird relation not natively converted: {r}]");
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Models;

public enum CalendarAttachmentKind { InlineBytes, LocalFileByValue, LinkOnly }

/// <summary>A classified calendar/task attachment. InlineBytes carries decoded bytes (embedded ByValue,
/// NOT hidden); LocalFileByValue carries a validated in-root file path; LinkOnly carries a
/// PreservedReference (remote URL or raw un-parseable ATTACH text) preserved in the body — never
/// embedded, never fetched.</summary>
public sealed record CalendarAttachment(
    CalendarAttachmentKind Kind, string FileName, string MimeType,
    byte[]? InlineData, string? LocalPath, string? PreservedReference);

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Models;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Guards subject sanitization + truncation (2026-07-07 scanpst root-cause, real-corpus
/// follow-ups). Two invariants scanpst enforces on the written subject family
/// (PidTagSubject / PidTagConversationTopic / PidTagNormalizedSubject):
///  - No raw control characters: PidTagSubject legitimately BEGINS with the MS-PST
///    subject-prefix marker 0x01 0x01 (added by the vendored Subject setter), so control
///    bytes coming from a mangled source header corrupt the prefix decoding.
///  - MAPI length cap: a subject long enough to push the contents-table row cell past the
///    heap allocation limit spills the cell to a subnode, which scanpst rejects as
///    "row doesn't match sub-object" (seen in the wild: a 2,700-char LinkedIn subject).
///    Outlook truncates subjects to 255 chars (including the 2-char prefix) — so must we.
/// </summary>
public class SubjectSanitizationTests
{
    private const int PidTagNormalizedSubject = 0x0E1D;
    private static readonly string SubjectPrefixMarker = new string(new[] { (char)0x01, (char)0x01 });

    [Fact]
    public void ControlCharacters_AreStrippedFromWrittenSubjectFamily()
    {
        // Raw control bytes seen in the wild, constructed explicitly (never as literals).
        string dirty = (char)0x01 + "" + (char)0x01 + "Weird" + (char)0x02 + " job ad" + (char)0x1F;
        (string? raw, string? topic, string? normalized) = WriteAndReadSubjects(Msg(dirty));

        // The vendored Subject setter re-adds the legitimate 0x01 0x01 empty-prefix marker.
        Assert.Equal(SubjectPrefixMarker + "Weird job ad", raw);
        Assert.Equal("Weird job ad", topic);
        Assert.Equal("Weird job ad", normalized);
    }

    [Fact]
    public void OverlongSubject_IsTruncatedToMapiLimit()
    {
        string longSubject = string.Concat(Enumerable.Repeat("Sesquipedalian subject segment. ", 90)); // ~2880 chars
        (string? raw, string? topic, string? normalized) = WriteAndReadSubjects(Msg(longSubject));

        Assert.NotNull(raw);
        Assert.True(raw!.Length <= 255,
            $"PidTagSubject length {raw.Length} exceeds the 255-char MAPI cap (incl. 2-char prefix) — " +
            "the contents-table row cell spills to a subnode and scanpst reports 'row doesn't match sub-object'");
        Assert.Equal(SubjectPrefixMarker + longSubject[..253], raw);
        Assert.Equal(longSubject[..253], topic);
        Assert.Equal(longSubject[..253], normalized);
    }

    private static MailMessage Msg(string subject) => new()
    {
        Subject = subject,
        From = new MailAddress { Name = "Sender", Email = "sender@example.com" },
        To = { new MailAddress { Name = "Rcpt", Email = "rcpt@example.com" } },
        Date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        TextBody = "Body.",
    };

    private static (string? Raw, string? Topic, string? Normalized) WriteAndReadSubjects(MailMessage msg)
    {
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-subject-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            IReadOnlyList<string> psts = RoundTripHarness.ConvertMessages(new[] { msg }, outDir);
            var pst = new PSTFile(psts[0], FileAccess.Read);
            try
            {
                PSTFolder? folder = FindFolderWithMessage(pst.TopOfPersonalFolders);
                Assert.NotNull(folder);
                var tc = folder!.GetContentsTable();
                MessageObject m = MessageObject.GetMessage(pst, new NodeID(tc.GetRowID(0)));
                return (
                    m.PC.GetStringProperty(PropertyID.PidTagSubject),
                    m.PC.GetStringProperty(PropertyID.PidTagConversationTopic),
                    m.PC.GetStringProperty((PropertyID)PidTagNormalizedSubject));
            }
            finally { pst.CloseFile(); }
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static PSTFolder? FindFolderWithMessage(PSTFolder root)
    {
        foreach (PSTFolder child in root.GetChildFolders())
        {
            if (child.MessageCount > 0) return child;
            if (FindFolderWithMessage(child) is { } found) return found;
        }
        return null;
    }
}

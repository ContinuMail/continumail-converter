// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Msf;
using MimeKit;

namespace Mail2Pst.Core.Parsing.Mime;

/// <summary>
/// Maps a parsed MIME message into the converter's neutral <see cref="MailMessage"/> model
/// (subject, addresses, date, bodies, attachments, threading headers, read-state, importance).
/// Intended as a low-level building block for source parsers such as <c>MboxParser</c> and a
/// future <c>EmlParser</c> — it is public for reuse, NOT a stable end-user API; no backward-
/// compatibility is promised yet.
/// </summary>
public class MimeMessageMapper
{
    private readonly long _tempFileThresholdBytes;
    private readonly bool _measureOnly;

    private const uint MozillaStatusRead = 0x0001;
    private const uint MozillaStatusReplied = 0x0002;
    private const uint MozillaStatusMarked = 0x0004;
    private const uint MozillaStatusForwarded = 0x1000;

    public MimeMessageMapper(long tempFileThresholdBytes = 4L * 1024 * 1024, bool measureOnly = false)
    {
        if (tempFileThresholdBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(tempFileThresholdBytes), tempFileThresholdBytes, "Temp-file threshold must be non-negative.");
        _tempFileThresholdBytes = tempFileThresholdBytes;
        _measureOnly = measureOnly;
    }

    // A missing Date header yields null; a present-but-unparseable one makes
    // MimeKit return DateTimeOffset.MinValue (0001-01-01). Treat both as "no
    // date" so a single bad message can't poison scan date ranges.
    private static DateTimeOffset? ParseDate(MimeMessage mime)
    {
        if (!mime.Headers.Contains(HeaderId.Date)) return null;
        DateTimeOffset date = mime.Date;
        return date == DateTimeOffset.MinValue ? null : date;
    }

    public MailMessage Map(MimeMessage mime, SourceReference sourceRef, List<string> warnings)
    {
        uint? mozillaStatus = ParseMozillaStatus(mime);
        return new MailMessage
        {
            Subject = mime.Subject,
            From = ToMailAddress(mime.From.Mailboxes.FirstOrDefault()),
            To = mime.To.Mailboxes.Select(ToMailAddressNonNull).ToList(),
            Cc = mime.Cc.Mailboxes.Select(ToMailAddressNonNull).ToList(),
            Bcc = mime.Bcc.Mailboxes.Select(ToMailAddressNonNull).ToList(),
            Date = ParseDate(mime),
            TextBody = mime.TextBody,
            HtmlBody = mime.HtmlBody,
            Attachments = ExtractAttachments(mime, warnings),
            Source = sourceRef,
            MessageId = MessageIdNormalizer.NormalizeForJoin(mime.MessageId),
            InReplyTo = MessageIdNormalizer.NormalizeForJoin(mime.InReplyTo),
            References = mime.References.Count > 0
                ? string.Join(" ", mime.References.Select(id => MessageIdNormalizer.NormalizeForJoin(id)).OfType<string>())
                : null,
            IsRead = ParseIsRead(mime, mozillaStatus),
            IsReplied = mozillaStatus is { } f1 && (f1 & MozillaStatusReplied) != 0,
            IsFlagged = mozillaStatus is { } f2 && (f2 & MozillaStatusMarked) != 0,
            IsForwarded = mozillaStatus is { } f3 && (f3 & MozillaStatusForwarded) != 0,
            Importance = ParseImportance(mime),
        };
    }

    private static MailImportance ParseImportance(MimeMessage mime)
    {
        if (mime.Importance == MessageImportance.High) return MailImportance.High;
        if (mime.Importance == MessageImportance.Low) return MailImportance.Low;

        // Importance == Normal is treated as "not explicitly set"; check X-Priority fallback.
        // mime.XPriority (distinct from mime.Priority) reads the X-Priority header and handles
        // decorated values like "1 (Highest)" that raw int-parse would miss.
        // XMessagePriority: Highest=1, High=2 → our High; Low=4, Lowest=5 → our Low; Normal=3.
        XMessagePriority xp = mime.XPriority;
        if (xp == XMessagePriority.Highest || xp == XMessagePriority.High) return MailImportance.High;
        if (xp == XMessagePriority.Low || xp == XMessagePriority.Lowest) return MailImportance.Low;

        return MailImportance.Normal;
    }

    private static uint? ParseMozillaStatus(MimeMessage mime)
    {
        string? value = mime.Headers["X-Mozilla-Status"];
        if (value is null) return null;
        return uint.TryParse(value.Trim(), System.Globalization.NumberStyles.HexNumber,
                             System.Globalization.CultureInfo.InvariantCulture, out uint flags)
            ? flags
            : null; // malformed -> null, same fall-through tolerance as before
    }

    private static bool ParseIsRead(MimeMessage mime, uint? mozillaStatus)
    {
        string? gmailLabels = mime.Headers["X-Gmail-Labels"];
        if (gmailLabels is not null)
        {
            // Gmail exports labels in the account's display language; match known locale variants.
            // English: "Unread", Danish: "Ulæste"
            return !gmailLabels.Split(',').Any(t =>
                t.Trim().Equals("Unread", StringComparison.OrdinalIgnoreCase) ||
                t.Trim().Equals("Ulæste", StringComparison.OrdinalIgnoreCase));
        }

        // Mozilla nsMsgMessageFlags.h: Read = 0x0001, New = 0x10000 (NOT 0x0001).
        // https://searchfox.org/comm-central/source/mailnews/base/public/nsMsgMessageFlags.h
        // Bit 0 set -> read; the NEW bit is 0x10000, unrelated. Do NOT "fix" this to == 0.
        if (mozillaStatus is { } flags)
            return (flags & MozillaStatusRead) != 0;

        string? status = mime.Headers["Status"];
        if (status is not null)
            return status.Contains('R', StringComparison.OrdinalIgnoreCase);

        return true;
    }

    /// <summary>
    /// Classifies each entity in <paramref name="mime"/>'s MIME tree as either
    /// body content (skipped) or an attachment/inline resource. Any
    /// unrecognized or non-body leaf MIME part is preserved as an attachment
    /// rather than dropped (a deliberate v1 fidelity choice). Per-attachment
    /// extraction failures are recorded in <paramref name="warnings"/> and the
    /// attachment is dropped without failing the whole message.
    /// </summary>
    internal List<MailAttachment> ExtractAttachments(MimeMessage mime, List<string> warnings)
    {
        var attachments = new List<MailAttachment>();
        int index = 0;

        foreach (MimeEntity entity in mime.BodyParts)
        {
            if (entity is MessagePart messagePart)
            {
                // An embedded message (message/rfc822) is a user attachment only when it
                // is explicitly marked as one. NDR/bounce mail wraps the original
                // undelivered message in a message/rfc822 with no attachment disposition
                // (commonly inside multipart/report, sometimes nested below the top level) —
                // surfacing it as attached-message.eml is the KB-001 phantom. Requiring an
                // explicit attachment disposition or a filename (which legitimate forwards
                // carry) suppresses it regardless of how deeply the report is nested.
                bool isExplicitAttachment =
                    (messagePart.ContentDisposition?.Disposition is { } messageDisposition
                        && messageDisposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase))
                    || !string.IsNullOrEmpty(messagePart.ContentDisposition?.FileName)
                    || !string.IsNullOrEmpty(messagePart.ContentType.Name);
                if (!isExplicitAttachment)
                    continue;

                index++;
                const string embeddedFileName = "attached-message.eml";
                const string embeddedMimeType = "message/rfc822";
                try
                {
                    using var messageBytes = new MemoryStream();
                    messagePart.Message.WriteTo(messageBytes);

                    attachments.Add(new MailAttachment
                    {
                        FileName = embeddedFileName,
                        MimeType = embeddedMimeType,
                        Content = ToAttachmentContent(messageBytes),
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add(FormatDroppedAttachmentWarning(index, embeddedFileName, embeddedMimeType, ex));
                }

                continue;
            }

            if (entity is not MimePart part)
            {
                continue;
            }

            // message/delivery-status (and similar message/* parts) in an NDR are
            // machine-readable delivery metadata, not user-visible attachments. Skip them
            // unless explicitly marked as an attachment — independent of report nesting,
            // so a deeper-nested multipart/report doesn't leak a phantom (KB-001).
            if (part.ContentType.MediaType.Equals("message", StringComparison.OrdinalIgnoreCase)
                && !(part.ContentDisposition?.Disposition is { } messageMetaDisposition
                     && messageMetaDisposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase)))
                continue;

            bool isAttachmentDisposition = part.ContentDisposition?.Disposition is { } disposition
                && disposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase);
            bool isBodyMimeType = part.ContentType.IsMimeType("text", "plain") || part.ContentType.IsMimeType("text", "html");

            // text/plain and text/html parts are body content — only treat them as
            // attachments if they carry an explicit filename or attachment disposition.
            // A Content-ID alone on a body-typed part is a body-part label (e.g.
            // LinkedIn's "Content-ID: text-body"), not an inline resource reference.
            bool isAttachment = isAttachmentDisposition
                || !string.IsNullOrEmpty(part.FileName)
                || !isBodyMimeType;

            if (!isAttachment)
            {
                continue;
            }

            index++;

            // part.FileName is string? (MimeKit is nullable-annotated); IsNullOrEmpty
            // has [NotNullWhen(false)], so fileName is non-null after this block.
            string? fileName = part.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"attachment-{index}{GuessExtension(part.ContentType)}";
            }

            string mimeType = part.ContentType.MimeType;

            if (part.Content is null)
            {
                warnings.Add(FormatDroppedAttachmentWarning(index, fileName, mimeType,
                    new InvalidOperationException("attachment part has no content data")));
                continue;
            }

            try
            {
                AttachmentContent content = DecodeContent(part);

                string? contentId = string.IsNullOrEmpty(part.ContentId) ? null : NormalizeContentId(part.ContentId);
                bool isInlineDisposition = part.ContentDisposition?.Disposition is { } partDisposition
                    && partDisposition.Equals(ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase);

                attachments.Add(new MailAttachment
                {
                    FileName = fileName,
                    MimeType = mimeType,
                    ContentId = contentId,
                    ContentLocation = part.ContentLocation?.ToString(),
                    IsInline = isInlineDisposition || contentId is not null,
                    Content = content,
                });
            }
            catch (Exception ex)
            {
                warnings.Add(FormatDroppedAttachmentWarning(index, fileName, mimeType, ex));
            }
        }

        return attachments;
    }

    // Scan measure-only: decode the part to get its exact length, retaining nothing (no buffer, no temp
    // file). Materialize mode keeps the existing memory/temp-spill behavior. Both run the decoder, so a
    // decode failure still throws and is recorded as a dropped-attachment warning by the caller.
    private AttachmentContent DecodeContent(MimeKit.MimePart part)
    {
        if (_measureOnly)
        {
            var counter = new CountingStream();
            part.Content!.DecodeTo(counter);
            return AttachmentContent.FromLengthOnly(counter.BytesWritten);
        }
        using var buffer = new MemoryStream();
        part.Content!.DecodeTo(buffer);
        return ToAttachmentContent(buffer);
    }

    // Routes attachment bytes to memory (small) or a temp file (large) to bound the
    // SUSTAINED memory of the bounded parse/write queue — a queued temp-backed attachment
    // holds only a path, not its bytes. This does NOT reduce PEAK memory: the part is
    // already fully decoded into `buffer` (a MemoryStream) before this point, and the temp
    // file is read back in full at write time (AttachmentContent.ReadAllBytes -> PstWriter).
    // It is queue-memory hygiene, not end-to-end streaming.
    private AttachmentContent ToAttachmentContent(MemoryStream buffer)
    {
        if (buffer.Length < _tempFileThresholdBytes)
            return AttachmentContent.FromBytes(buffer.ToArray());

        string tempPath = Path.Combine(Path.GetTempPath(), $"mail2pst-{Guid.NewGuid()}");
        buffer.Position = 0;
        try
        {
            using (var tempFile = File.Create(tempPath))
                buffer.CopyTo(tempFile);
        }
        catch (Exception)
        {
            try { File.Delete(tempPath); } catch (Exception) { }
            throw;
        }
        return AttachmentContent.FromTempFile(tempPath, buffer.Length);
    }

    /// <summary>
    /// Formats the warning recorded when an attachment is dropped due to an
    /// extraction failure.
    /// </summary>
    private static string FormatDroppedAttachmentWarning(int index, string fileName, string mimeType, Exception ex) =>
        $"Dropped attachment #{index} '{fileName}' ({mimeType}): {ex.Message}";

    /// <summary>
    /// Returns a best-effort file extension (including the leading '.') for a
    /// part with no filename, derived from its MIME subtype (e.g. "image/png"
    /// -> ".png"). Returns "" for "octet-stream" or for subtypes that aren't a
    /// single alphanumeric token (e.g. "svg+xml"), since those wouldn't make a
    /// sensible bare extension.
    /// </summary>
    private static string GuessExtension(ContentType contentType)
    {
        string subtype = contentType.MediaSubtype;
        if (string.IsNullOrEmpty(subtype) || !subtype.All(char.IsLetterOrDigit))
        {
            return string.Empty;
        }

        if (subtype.Equals("octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return "." + subtype.ToLowerInvariant();
    }

    /// <summary>
    /// Strips the optional "cid:" prefix and surrounding angle brackets from a
    /// Content-ID/cid: reference, so stored ContentId values can be compared
    /// directly against each other regardless of which form they came from.
    /// Note: <c>part.ContentId</c> (from MIME headers) never has a "cid:"
    /// prefix in practice — that prefix only appears in HTML
    /// <c>src="cid:..."</c> references. The "cid:"-stripping branch here is
    /// defensive/future-proofing, for if such an HTML reference is ever
    /// normalized through this same helper.
    /// </summary>
    private static string NormalizeContentId(string contentId)
    {
        contentId = contentId.Trim();

        if (contentId.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
        {
            contentId = contentId.Substring(4);
        }

        if (contentId.StartsWith("<") && contentId.EndsWith(">"))
        {
            contentId = contentId.Substring(1, contentId.Length - 2);
        }

        return contentId;
    }

    private static MailAddress? ToMailAddress(MailboxAddress? mailbox) =>
        mailbox is null ? null : ToMailAddressNonNull(mailbox);

    private static MailAddress ToMailAddressNonNull(MailboxAddress mailbox) =>
        new() { Name = mailbox.Name, Email = mailbox.Address };
}

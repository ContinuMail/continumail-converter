// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Progress;
using Mail2Pst.Core.Reporting;
using PSTFileFormat;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Writes planned messages into one or more PST files, seeded from a copy of
/// the blank PST template. A single <see cref="PstOutputPlan"/> may produce
/// multiple physical files if it exceeds <see cref="PstOutputPlan.MaxSizeBytes"/>
/// (see Task 9).
/// </summary>
public class PstWriter
{
    private readonly string _templatePath;

    // Number of successfully-written messages between on-disk size checks.
    private readonly int _checkIntervalMessages;

    // Number of successfully-written messages between lightweight progress events
    // (decoupled from the flush/size-check interval so the GUI bar moves fluidly).
    private readonly int _progressIntervalMessages;

    // Parsed-but-not-yet-written messages buffered between the parser thread
    // and the PST-writer thread. Large enough to keep the writer busy during
    // brief parse stalls; small enough that we don't hold many messages in
    // memory simultaneously.
    private const int ParseQueueCapacity = 32;

    public PstWriter(string templatePath, int checkIntervalMessages = 500, int progressIntervalMessages = 25)
    {
        if (checkIntervalMessages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(checkIntervalMessages));
        }

        if (progressIntervalMessages < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(progressIntervalMessages));
        }

        _templatePath = templatePath;
        _checkIntervalMessages = checkIntervalMessages;
        _progressIntervalMessages = progressIntervalMessages;
    }

    public List<string> WritePlan(PstOutputPlan plan, IEnumerable<PlannedMessage> messages, string outputDirectory, ConversionReport report, int totalMessages = -1, Action<ConversionProgressEvent>? onProgress = null, CancellationToken cancellationToken = default)
    {
        // Pre-flight: if already cancelled, create nothing so DeletedFiles stays empty.
        cancellationToken.ThrowIfCancellationRequested();

        var partManager = new PstPartManager(
            _templatePath, plan.Name, outputDirectory, plan.MaxSizeBytes, _checkIntervalMessages, WriteMessageCore);
        var throttler = new ProgressThrottler(onProgress, totalMessages);

        // Producer thread parses MIME (CPU-bound); this consumer writes PST (I/O-bound).
        // The bounded channel back-pressures the parser so it can't race far ahead.
        using var cts = new CancellationTokenSource();
        using var queue = new BlockingCollection<PlannedMessage>(boundedCapacity: ParseQueueCapacity);
        Exception? producerException = null;

        var producer = Task.Run(() =>
        {
            try
            {
                foreach (PlannedMessage msg in messages)
                    queue.Add(msg, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { producerException = ex; }
            finally { queue.CompleteAdding(); }
        });

        bool cancelled = false;
        ExceptionDispatchInfo? faultCapture = null;
        string? currentSource = null;
        string? currentFolder = null;
        long estimatedOutputBytes = 0;   // run-wide; NEVER reset on split
        int messagesSinceProgress = 0;
        try
        {
            // Pre-create a folder for every mapped source when IncludeEmptyFolders, so an
            // empty source still appears as an empty folder (part 1 only — see spec inv. 6).
            // Both branches yield IReadOnlyList<string>[] at runtime (.ToArray() / Array.Empty),
            // giving the conditional a clean common type that satisfies IReadOnlyList<IReadOnlyList<string>>.
            IReadOnlyList<IReadOnlyList<string>> emptyFolders = plan.IncludeEmptyFolders
                ? plan.SourceMappings.Select(m => m.TargetFolderPath).ToArray()
                : Array.Empty<IReadOnlyList<string>>();
            partManager.Begin(emptyFolders);

            foreach (PlannedMessage planned in queue.GetConsumingEnumerable())
            {
                currentSource = planned.Message.Source.SourcePath;
                currentFolder = FolderPathDisplay.Join(planned.TargetFolderPath);

                long messageSize = 0;
                bool written = false;
                try
                {
                    // Cancellation BEFORE the predictive split so a cancelled run never
                    // creates an extra part. Inside the try so finally always disposes this
                    // dequeued message's attachments even if cancel fires before the write.
                    cancellationToken.ThrowIfCancellationRequested();

                    messageSize = EstimateMessageSize(planned.Message);

                    if (partManager.ShouldSplitBefore(messageSize))
                        partManager.FlushAndSplit();

                    partManager.Write(planned.TargetFolderPath, planned.Message);
                    report.RecordConverted();
                    written = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (IsRecoverableWriteError(ex))
                {
                    report.RecordSkipped(planned.Message.Source, ex.Message);
                }
                finally
                {
                    foreach (MailAttachment attachment in planned.Message.Attachments)
                        attachment.Content.Dispose();
                }

                if (!written) continue;

                estimatedOutputBytes += messageSize;
                partManager.OnWritten(messageSize);
                messagesSinceProgress++;
                if (messagesSinceProgress >= _progressIntervalMessages)
                {
                    throttler.Emit(report, currentSource, currentFolder, estimatedOutputBytes);
                    messagesSinceProgress = 0;
                }

                if (partManager.CheckpointDue)
                {
                    partManager.Flush();
                    throttler.Emit(report, currentSource, currentFolder, estimatedOutputBytes);
                    partManager.TrySplitOrResumeAfterFlush();   // both branches leave the part write-ready
                }
            }

            partManager.Finish();
            throttler.Emit(report, currentSource, currentFolder, estimatedOutputBytes);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancel: stop, let `finally` close the handle, delete in-progress part below.
            cancelled = true;
            cts.Cancel();
        }
        catch (Exception ex)
        {
            // Fatal write/setup failure: capture (preserving stack), stop the producer, let
            // `finally` close the handle, delete the in-progress part below.
            faultCapture = ExceptionDispatchInfo.Capture(ex);
            cts.Cancel();
        }
        finally
        {
            producer.Wait();
            partManager.Close();

            // Drain any messages still queued after a fatal error and dispose their
            // (possibly temp-backed) attachments so temp files don't linger.
            while (queue.TryTake(out PlannedMessage? leftover))
            {
                foreach (MailAttachment attachment in leftover.Message.Attachments)
                    attachment.Content.Dispose();
            }
        }

        if (cancelled)
        {
            partManager.DeleteCurrentPart();
            report.RecordDeletedFile(partManager.CurrentPath);
            report.AddOutputFiles(partManager.OutputFiles.Where(p => !string.Equals(p, partManager.CurrentPath, StringComparison.Ordinal)));
            throw new OperationCanceledException(cancellationToken);
        }

        // Fatal path: consumer fault (faultCapture) or producer/parse fault. Handle closed,
        // so delete only the in-progress part (CurrentPath is always the newest part), keep
        // completed parts, rethrow the ORIGINAL exception so ConversionRunner emits `error`.
        ExceptionDispatchInfo? fatal = faultCapture
            ?? (producerException is not null ? ExceptionDispatchInfo.Capture(producerException) : null);
        if (fatal is not null)
        {
            partManager.DeleteCurrentPart();
            fatal.Throw();
        }

        return partManager.OutputFiles.ToList();
    }

    private static string GetPlainTextBody(MailMessage message)
    {
        if (!string.IsNullOrEmpty(message.TextBody))
        {
            return message.TextBody;
        }

        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            return HtmlToPlainText(message.HtmlBody);
        }

        return string.Empty;
    }

    private static readonly string[] ThreadingPrefixes = { "Re:", "Fwd:", "FW:", "AW:", "SV:", "TR:" };

    private static string StripThreadingPrefixes(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return string.Empty;
        string current = subject.Trim();
        bool stripped;
        do
        {
            stripped = false;
            foreach (string prefix in ThreadingPrefixes)
            {
                if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    current = current.Substring(prefix.Length).Trim();
                    stripped = true;
                    break;
                }
            }
        } while (stripped);
        return current;
    }

    // Lightweight HTML-to-text fallback for clients that don't render PidTagHtml; not a full HTML parser.
    // internal (not private) so it can be unit-tested directly; visible to the test assembly via
    // InternalsVisibleTo("Mail2Pst.Core.Tests"). Guarded against regex ReDoS: see HtmlRegexTimeout /
    // HtmlScriptStyleStripMaxChars. Degraded plain text is acceptable; this method must never throw
    // from the guard path.
    internal static string HtmlToPlainText(string html)
    {
        // Script/style removal is the backtracking vector. Skip it above the size cap;
        // otherwise run it under a timeout and fall back to the un-stripped html on timeout.
        string withoutScriptsAndStyles = html;
        if (html.Length <= HtmlScriptStyleStripMaxChars)
        {
            try
            {
                withoutScriptsAndStyles = ScriptStyleRegex.Replace(html, " ");
            }
            catch (RegexMatchTimeoutException)
            {
                withoutScriptsAndStyles = html;
            }
        }

        string withoutTags;
        try
        {
            withoutTags = HtmlTagRegex.Replace(withoutScriptsAndStyles, " ");
        }
        catch (RegexMatchTimeoutException)
        {
            withoutTags = withoutScriptsAndStyles;
        }

        string decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"[ \t]+", " ").Trim();
    }

    // HtmlToPlainText ReDoS guard. The script/style removal uses a lazy match that
    // can backtrack badly on pathological HTML (e.g. an unclosed <script>). Bound it
    // with a regex timeout and skip it entirely above a size cap; the <[^>]*> tag
    // strip is linear but carries a timeout defensively.
    private const int HtmlScriptStyleStripMaxChars = 1_000_000;
    private static readonly TimeSpan HtmlRegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style)\b[^>]*>.*?</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        HtmlRegexTimeout);

    private static readonly Regex HtmlTagRegex = new(
        @"<[^>]*>",
        RegexOptions.CultureInvariant,
        HtmlRegexTimeout);

    // Per-message PST structural overhead not captured by the content byte counts below:
    // node B-tree entry (~128 B) + property-context heap for ~20 properties (~512 B) +
    // recipient table for 1-2 recipients (~256 B) + the fixed string properties WriteMessage
    // always sets — sender, PidTagInternetMessageId, In-Reply-To/References, conversation
    // topic ×2 (~512 B) + attachment subnode pointer table (~256 B) + block alignment/padding
    // (~384 B) ≈ 2048 B. An order-of-magnitude estimate, NOT a measurement — the on-disk
    // checkpoint in WritePlan is the authoritative backstop, so this only has to be roughly right.
    private const long PerMessageOverheadBytes = 2048;

    // Per-attachment overhead beyond the raw content bytes: the attachment subnode plus its
    // 8-12 metadata properties (long/short filename, display name, extension, MIME tag, method,
    // size, optional content-id/location/flags/hidden). Dwarfed by real attachment content for
    // typical messages; the backstop covers the rare many-tiny-inline-attachments case where
    // this flat constant under-counts the aggregate.
    private const long PerAttachmentOverheadBytes = 512;

    // MAPI bit flags for PidTagMessageFlags (MS-OXCMSG) and the inline-attachment
    // PidTagAttachFlags value (MS-OXCMSG ATT_MHTML_REF). Named here so the bit math
    // at the write sites reads intentionally instead of as raw hex.
    private const int MSGFLAG_READ = 0x0001;
    private const int MSGFLAG_HASATTACH = 0x0010;
    private const int ATT_MHTML_REF = 4;

    internal static long EstimateMessageSize(MailMessage message)
    {
        // PidTagSubject is stored as Unicode (UTF-16) → 2 bytes/char.
        long size = 2L * (message.Subject?.Length ?? 0);

        // PidTagBody = GetPlainTextBody(message): TextBody verbatim if present, else the HTML→text
        // fallback (HtmlToPlainText output is always <= HtmlBody.Length). Estimate its char count
        // WITHOUT running the regex converter a second time (WriteMessage already runs it):
        // TextBody.Length when present, otherwise HtmlBody.Length as a cheap UPPER-BOUND proxy —
        // a deliberate slight over-count, safe for a cap estimate (splits early, not late).
        // PidTagBody is Unicode → 2 bytes/char.
        int plainTextChars = !string.IsNullOrEmpty(message.TextBody)
            ? message.TextBody!.Length
            : (message.HtmlBody?.Length ?? 0);
        size += 2L * plainTextChars;

        // PidTagHtml is written as UTF-8 BYTES in WriteMessage, not UTF-16 code units.
        if (!string.IsNullOrEmpty(message.HtmlBody))
            size += Encoding.UTF8.GetByteCount(message.HtmlBody);

        foreach (MailAttachment attachment in message.Attachments)
            size += attachment.Content.Length + PerAttachmentOverheadBytes;

        return size + PerMessageOverheadBytes;
    }

    // Per-message write, extracted as an overridable seam so tests can inject a
    // deterministic write failure without contriving exotic message data. Production
    // behavior is unchanged — it simply calls WriteMessage. internal: visible to the
    // test assembly via InternalsVisibleTo("Mail2Pst.Core.Tests").
    internal virtual void WriteMessageCore(PSTFile file, PSTFolder folder, MailMessage message)
        => WriteMessage(file, folder, message);

    // A PST writer failure is fatal unless PROVEN to be one bad message. By the time we
    // write, the message has parsed successfully, so a write failure is almost certainly
    // a bug, a vendored PST-library failure, invalid internal state, or a filesystem/temp
    // problem — all of which must surface as fatal (mirroring the parse-side taxonomy in
    // MboxParser). This allowlist is intentionally EMPTY: add a specific exception type
    // ONLY after a real data-driven write failure is observed and proven safely skippable.
    // Keeping the predicate + catch-filter makes that a one-line change. This is a
    // deliberate, documented extension seam — NOT dead code to "clean up".
    internal static bool IsRecoverableWriteError(Exception ex) => false;

    private static void WriteMessage(PSTFile file, PSTFolder folder, MailMessage message)
    {
        Note note = Note.CreateNewNote(file, folder.NodeID);
        note.Subject = message.Subject ?? string.Empty;

        if (message.From is not null)
        {
            // A malformed/empty From header (e.g. "From: <>") can yield an empty email.
            // Only claim an SMTP sender address when we actually have one — otherwise we'd
            // write an "SMTP" address type with an empty address, which Outlook renders oddly.
            string email = message.From.Email;
            bool hasEmail = !string.IsNullOrWhiteSpace(email);
            string senderName = message.From.Name ?? (hasEmail ? email : string.Empty);
            note.SenderName = senderName;
            note.SentRepresentingName = senderName;
            if (hasEmail)
            {
                note.SenderAddressType = "SMTP";
                note.SenderEmailAddress = email;
                note.SentRepresentingAddressType = "SMTP";
                note.SentRepresentingEmailAddress = email;
            }
        }

        note.Body = GetPlainTextBody(message);

        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            note.PC.SetBytesProperty(PropertyID.PidTagHtml, Encoding.UTF8.GetBytes(message.HtmlBody));
            note.PC.SetInt32Property(PropertyID.PidTagNativeBody, 3);
            note.InternetCodepage = 65001;
        }

        if (message.Date.HasValue)
        {
            DateTime utcDate = ClampToFileTimeRange(message.Date.Value.UtcDateTime);
            note.ClientSubmitTime = utcDate;
            note.MessageDeliveryTime = utcDate;
        }

        static IEnumerable<MessageRecipient> ToRecipients(IEnumerable<MailAddress> addresses, RecipientType type) =>
            addresses.Select(address =>
                new MessageRecipient(address.Name ?? address.Email, address.Email, isOrganizer: false, type));

        List<MessageRecipient> recipients = ToRecipients(message.To, RecipientType.To)
            .Concat(ToRecipients(message.Cc, RecipientType.Cc))
            .Concat(ToRecipients(message.Bcc, RecipientType.Bcc))
            .ToList();

        if (recipients.Count > 0)
        {
            note.AddRecipients(recipients);
        }

        if (message.MessageId is not null)
            note.PC.SetStringProperty(PropertyID.PidTagInternetMessageId, message.MessageId);
        if (message.InReplyTo is not null)
            note.PC.SetStringProperty((PropertyID)0x1042, message.InReplyTo);
        if (message.References is not null)
            note.PC.SetStringProperty((PropertyID)0x1039, message.References);

        note.PC.SetInt32Property(PropertyID.PidTagImportance, (int)message.Importance);
        note.PC.SetInt32Property(PropertyID.PidTagPriority, (int)message.Importance - 1);

        string normalizedSubject = StripThreadingPrefixes(message.Subject);
        note.PC.SetStringProperty(PropertyID.PidTagConversationTopic, normalizedSubject);
        note.PC.SetStringProperty((PropertyID)0x0E1D, normalizedSubject);

        foreach (MailAttachment attachment in message.Attachments)
        {
            WriteAttachment(file, note, attachment);
        }

        // Set message flags AFTER attachments are written: the vendored
        // AddAttachment unconditionally sets MSGFLAG_HASATTACH for any attachment
        // (including inline ones), so we must have the final word here. We only
        // control the two bits we own (MSGFLAG_READ, MSGFLAG_HASATTACH) and
        // preserve everything else Note.CreateNewNote/AddAttachment set.
        int messageFlags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags) ?? 0;
        if (message.IsRead) messageFlags |= MSGFLAG_READ;
        else messageFlags &= ~MSGFLAG_READ;
        // Only set MSGFLAG_HASATTACH for non-inline attachments. Inline (CID)
        // images are part of the HTML body, not user-visible attachments, so a
        // message whose only attachments are inline must not show a paperclip.
        if (message.Attachments.Any(a => !a.IsInline)) messageFlags |= MSGFLAG_HASATTACH;
        else messageFlags &= ~MSGFLAG_HASATTACH;
        note.PC.SetInt32Property(PropertyID.PidTagMessageFlags, messageFlags);

        // Follow-up flag (Thunderbird "marked"/starred). Independent of MSGFLAG_READ.
        if (message.IsFlagged)
        {
            note.PC.SetInt32Property(PropertyID.PidTagFlagStatus, 2);   // followupFlagged
            note.PC.SetInt32Property(PropertyID.PidTagFollowupIcon, 6); // red
        }

        // Last verb (replied/forwarded). PidTagLastVerbExecuted is single-valued: when both are
        // set we prefer reply (102) — replied is the more user-visible action (documented limitation).
        int? lastVerb = message.IsReplied ? 102 : (message.IsForwarded ? 104 : (int?)null);
        if (lastVerb is { } verb)
        {
            note.PC.SetInt32Property(PropertyID.PidTagLastVerbExecuted, verb);
            note.PC.SetDateTimeProperty(PropertyID.PidTagLastVerbExecutionTime, LastVerbTime(message));
        }

        if (message.Categories.Count > 0)
        {
            ushort keywordsId = PSTFileFormat.PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords");
            note.PC.SetMultiStringProperty((PSTFileFormat.PropertyID)keywordsId, message.Categories);
        }

        note.SaveChanges();
        folder.AddMessage(note);
        // folder.SaveChanges() is intentionally NOT called per message: PstPartManager batches it
        // (SaveDirtyFolders) to each checkpoint/split/finish flush, before EndSavingChanges. Saving
        // a folder's whole contents table per message is ~O(n^2); batching is the E3 perf win.
    }

    private static void WriteAttachment(PSTFile file, Note note, MailAttachment a)
    {
        note.CreateSubnodeBTreeIfNotExist();
        AttachmentObject attachment = AttachmentObject.CreateNewAttachmentObject(file, note.SubnodeBTree);

        attachment.PC.SetStringProperty(PropertyID.PidTagAttachLongFilename, a.FileName);
        attachment.PC.SetStringProperty(PropertyID.PidTagAttachFilename, a.FileName);
        attachment.PC.SetStringProperty(PropertyID.PidTagDisplayName, a.FileName);
        attachment.PC.SetStringProperty(PropertyID.PidTagAttachExtension, Path.GetExtension(a.FileName));
        attachment.PC.SetStringProperty(PropertyID.PidTagAttachMimeTag, a.MimeType);
        attachment.PC.SetInt32Property(PropertyID.PidTagAttachMethod, (int)AttachMethod.ByValue);
        attachment.PC.SetBytesProperty(PropertyID.PidTagAttachData, a.Content.ReadAllBytes());
        attachment.PC.SetInt32Property(PropertyID.PidTagAttachSize, (int)a.Content.Length);

        if (!string.IsNullOrEmpty(a.ContentId))
        {
            attachment.PC.SetStringProperty(PropertyID.PidTagAttachContentId, a.ContentId);
        }

        if (!string.IsNullOrEmpty(a.ContentLocation))
        {
            attachment.PC.SetStringProperty(PropertyID.PidTagAttachContentLocation, a.ContentLocation);
        }

        if (a.IsInline)
        {
            attachment.PC.SetInt32Property(PropertyID.PidTagAttachFlags, ATT_MHTML_REF);
            // Hide inline (CID) images from the attachment list so Outlook does
            // not render a paperclip for body-embedded images.
            attachment.PC.SetBooleanProperty(PropertyID.PidTagAttachmentHidden, true);
        }

        // attachment.PC's property writes above only update the in-memory
        // property context; SaveChanges(note.SubnodeBTree) flushes it to the
        // attachment's data tree and updates the entry this subnode occupies
        // in the note's subnode B-tree (Subnode.SaveChanges(SubnodeBTree)),
        // so the attachment's properties survive a reopen of the PST.
        attachment.SaveChanges(note.SubnodeBTree);
        note.AddAttachment(attachment);
    }

    // Windows FILETIME epoch is 1601-01-01. DateTime.ToFileTimeUtc() throws
    // ArgumentOutOfRangeException for dates before that, which would skip the
    // whole message. Clamp to the valid range instead.
    private static readonly DateTime MinFileTime = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime ClampToFileTimeRange(DateTime utcDate) =>
        utcDate < MinFileTime ? MinFileTime : utcDate;

    // The verb timestamp is metadata, not content: when the message has no date, "now" is a better
    // fallback than omitting it (Outlook may not render the reply/forward arrow without a time).
    private static DateTime LastVerbTime(MailMessage message)
    {
        DateTime utcDate = message.Date?.UtcDateTime ?? DateTime.UtcNow;
        return ClampToFileTimeRange(utcDate);
    }
}

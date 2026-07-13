// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using PSTFileFormat;

namespace Mail2Pst.Core.Reverse;

/// <summary>
/// Streams mail messages from a PST for the reverse (PST→Thunderbird) export — one message alive at a
/// time, so a large PST is never fully materialized. Based on the Integration.Tests PstReader.
/// Failure policy: a non-mail folder with messages, or a per-message read failure, is reported via
/// onWarning and skipped; OutOfMemory/process failures and a fatal open/root-walk failure propagate.
/// </summary>
public static class PstMailReader
{
    /// <summary>Lazy: the PST stays open while iterating. Attachment OpenRead must be called during iteration.</summary>
    public static IEnumerable<PstMailItem> EnumerateMessages(
        string pstPath, Action<string>? onWarning = null, Action<ExportSkip>? onSkipped = null)
    {
        var pst = new PSTFile(pstPath, FileAccess.Read);            // fatal on failure -> propagate
        try
        {
            ushort? keywordsId = PropertyNameToIDMap.ResolveStringNamedProperty(pst, 2, "Keywords");
            foreach (PSTFolder child in pst.TopOfPersonalFolders.GetChildFolders())
                foreach (PstMailItem item in EnumerateFolder(child, new List<string>(), keywordsId, onWarning, onSkipped))
                    yield return item;
        }
        finally { pst.CloseFile(); }
    }

    /// <summary>
    /// The STRUCTURE AUTHORITY for the reverse export: walks the whole tree from TopOfPersonalFolders and
    /// returns every mail folder's path INCLUDING empty ones (which <see cref="EnumerateMessages"/> never
    /// yields). Recurses THROUGH non-mail containers so their mail descendants are returned with their full
    /// path (the container itself is not a mail folder and is not returned; MboxTreePlanner synthesizes it as
    /// a structural parent). Reads no messages and does NOT modify the store. A fatal open/root-walk failure
    /// propagates; a corrupt child-walk is reported via <paramref name="onWarning"/> and that subtree stops.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> EnumerateFolders(string pstPath, Action<string>? onWarning = null)
    {
        var pst = new PSTFile(pstPath, FileAccess.Read);          // fatal on failure -> propagate
        try
        {
            var result = new List<IReadOnlyList<string>>();
            foreach (PSTFolder child in pst.TopOfPersonalFolders.GetChildFolders())
                CollectFolders(child, new List<string>(), result, onWarning);
            return result;
        }
        finally { pst.CloseFile(); }
    }

    private static void CollectFolders(
        PSTFolder folder, List<string> parentPath, List<IReadOnlyList<string>> acc, Action<string>? onWarning)
    {
        var path = new List<string>(parentPath) { folder.DisplayName };
        if (folder is MailFolder)
            acc.Add(path);                                        // mail folder (possibly empty) -> structure

        List<PSTFolder> children;
        try { children = folder.GetChildFolders(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) // corrupt subtree: warn + stop this branch
        {
            onWarning?.Invoke(
                $"could not read subfolders of '{FolderPathDisplay.Join(path)}': {ex.GetType().Name}: {ex.Message}");
            return;
        }
        foreach (PSTFolder c in children)
            CollectFolders(c, path, acc, onWarning);
    }

    /// <summary>
    /// Reports a per-message read failure. When <paramref name="onSkipped"/> is supplied (the export runner),
    /// records a STRUCTURED skip ONLY — it is NOT also emitted as a warning, so a skip is not double-counted.
    /// When <paramref name="onSkipped"/> is null (legacy 2-arg callers such as ReadAllForTests), the
    /// human-readable warning is emitted instead. Reason carries the exception type name + message.
    /// </summary>
    internal static void ReportMessageReadFailure(
        IReadOnlyList<string> path, int index, Exception ex, Action<string>? onWarning, Action<ExportSkip>? onSkipped)
    {
        string display = string.Join(" / ", path);
        string reason = $"{ex.GetType().Name}: {ex.Message}";
        if (onSkipped is not null)
            onSkipped(new ExportSkip(display, index, reason));
        else
            onWarning?.Invoke($"skipped message {index} in '{display}': {reason}");
    }

    private static IEnumerable<PstMailItem> EnumerateFolder(
        PSTFolder folder, List<string> parentPath, ushort? keywordsId, Action<string>? onWarning,
        Action<ExportSkip>? onSkipped)
    {
        var path = new List<string>(parentPath) { folder.DisplayName };

        if (folder is MailFolder mf)
        {
            int count = 0;
            try { count = mf.MessageCount; }
            catch (Exception ex) when (ex is not OutOfMemoryException)       // corrupt contents table: warn + skip
            {
                onWarning?.Invoke(
                    $"could not read message count of '{string.Join(" / ", path)}': {ex.GetType().Name}: {ex.Message}");
                count = 0;
            }
            for (int i = 0; i < count; i++)
            {
                PstMailMessage? msg = null;
                try { msg = ReadNote(mf.GetNote(i), keywordsId, onWarning); }
                catch (Exception ex) when (ex is not OutOfMemoryException)   // bad message: structured skip or legacy warn
                {
                    ReportMessageReadFailure(path, i, ex, onWarning, onSkipped);
                }
                if (msg is not null)
                    yield return new PstMailItem(path, msg);                  // yield OUTSIDE the try/catch
            }
        }
        else
        {
            int nonMailCount = 0;
            try { nonMailCount = folder.MessageCount; }
            catch (Exception ex) when (ex is not OutOfMemoryException)       // corrupt contents table: warn + skip
            {
                onWarning?.Invoke(
                    $"could not read message count of '{string.Join(" / ", path)}': {ex.GetType().Name}: {ex.Message}");
                nonMailCount = 0;
            }
            if (nonMailCount > 0)                                            // non-mail with messages: warn + skip
            {
                onWarning?.Invoke(
                    $"folder '{string.Join(" / ", path)}' has {nonMailCount} message(s) but is not a mail " +
                    "folder (container class differs); skipping its messages.");
            }
        }

        // NOTE: C# iterators forbid `yield` inside a catch clause, so this must NOT `yield break` in the
        // catch. On a corrupt child-walk we warn and fall through with an empty child list (skips subtree).
        List<PSTFolder> children;
        try { children = folder.GetChildFolders(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)           // corrupt subtree: warn + skip
        {
            onWarning?.Invoke(
                $"could not read subfolders of '{string.Join(" / ", path)}': {ex.GetType().Name}: {ex.Message}");
            children = new List<PSTFolder>();
        }
        foreach (PSTFolder c in children)
            foreach (PstMailItem item in EnumerateFolder(c, path, keywordsId, onWarning, onSkipped))
                yield return item;
    }

    private static PstMailMessage ReadNote(Note note, ushort? keywordsId, Action<string>? onWarning)
    {
        var recipients = new List<PstRecipient>();
        for (int i = 0; i < note.RecipientCount; i++)
        {
            MessageRecipient r = note.GetRecipient(i);
            int? typeRaw = note.RecipientsTable!.GetInt32Property(i, PropertyID.PidTagRecipientType);
            recipients.Add(new PstRecipient(r.EmailAddress ?? string.Empty, r.DisplayName, MapKind(typeRaw, onWarning)));
        }

        var attachments = new List<PstAttachment>();
        for (int i = 0; i < note.AttachmentCount; i++)
        {
            AttachmentObject att = note.GetAttachmentObject(i);
            string? contentId = att.PC.GetStringProperty(PropertyID.PidTagAttachContentId);
            bool hidden = att.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden) ?? false;
            string name = att.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename)
                          ?? att.PC.GetStringProperty(PropertyID.PidTagDisplayName) ?? string.Empty;
            string? mime = att.PC.GetStringProperty(PropertyID.PidTagAttachMimeTag);
            attachments.Add(new PstAttachment(
                name, mime, contentId,
                IsInline: hidden || !string.IsNullOrEmpty(contentId),
                OpenRead: () => new MemoryStream(att.PC.GetBytesProperty(PropertyID.PidTagAttachData) ?? Array.Empty<byte>()),
                Length: null)
            {
                ContentLocation = att.PC.GetStringProperty(PropertyID.PidTagAttachContentLocation),
            });
        }

        byte[]? html = note.PC.GetBytesProperty(PropertyID.PidTagHtml);
        DateTimeOffset? date = null;
        DateTime? submit = note.PC.GetDateTimeProperty(PropertyID.PidTagClientSubmitTime);
        if (submit.HasValue)
            date = new DateTimeOffset(DateTime.SpecifyKind(submit.Value, DateTimeKind.Utc));

        int msgFlags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags) ?? 0;
        int? lastVerb = note.PC.GetInt32Property(PropertyID.PidTagLastVerbExecuted);

        IReadOnlyList<string> categories = Array.Empty<string>();
        if (keywordsId is ushort kid)
        {
            var rec = note.PC.GetRecordByPropertyID((PropertyID)kid);
            if (rec != null)
                categories = PropertyContext.DeserializeMultiString(note.PC.GetExternalRecordData(rec));
        }

        return new PstMailMessage(
            Subject: note.Subject,
            FromAddress: note.PC.GetStringProperty(PropertyID.PidTagSentRepresentingEmailAddress),
            Recipients: recipients,
            Date: date,
            MessageId: note.PC.GetStringProperty(PropertyID.PidTagInternetMessageId),
            InReplyTo: note.PC.GetStringProperty(PropertyID.PidTagInReplyToId),
            References: note.PC.GetStringProperty(PropertyID.PidTagInternetReferences),
            PlainBody: note.Body,
            HtmlBody: html is { Length: > 0 } ? html : null,
            InternetCodepage: note.PC.GetInt32Property(PropertyID.PidTagInternetCodepage),
            TransportHeaders: note.PC.GetStringProperty(PropertyID.PidTagTransportMessageHeaders),
            IsRead: (msgFlags & 0x0001) != 0,
            IsReplied: lastVerb is 102 or 103,
            IsForwarded: lastVerb is 104,
            Categories: categories,
            Attachments: attachments)
        {
            FromName = note.PC.GetStringProperty(PropertyID.PidTagSentRepresentingName),
        };
    }

    private static PstRecipientKind MapKind(int? raw, Action<string>? onWarning)
    {
        if (!raw.HasValue) return PstRecipientKind.To;
        return (RecipientType)(uint)raw.Value switch
        {
            RecipientType.To => PstRecipientKind.To,
            RecipientType.Cc => PstRecipientKind.Cc,
            RecipientType.Bcc => PstRecipientKind.Bcc,
            var other => Warn(other, raw.Value, onWarning),
        };

        static PstRecipientKind Warn(RecipientType other, int rawValue, Action<string>? onWarning)
        {
            onWarning?.Invoke($"unexpected recipient type raw={rawValue} enum='{other}' -> treated as To");
            return PstRecipientKind.To;
        }
    }

    /// <summary>TEST ONLY. Materializes all folders/messages (attachment bytes still lazy via OpenRead,
    /// which must be read during the underlying enumeration — so tests that assert attachment payload
    /// should use EnumerateMessages directly, not this helper).</summary>
    public static IReadOnlyList<PstMailFolder> ReadAllForTests(string pstPath, Action<string>? onWarning = null)
    {
        var byKey = new Dictionary<string, (List<string> Path, List<PstMailMessage> Msgs)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (PstMailItem item in EnumerateMessages(pstPath, onWarning))
        {
            string key = FolderPathKey.Join(item.FolderPath);
            if (!byKey.TryGetValue(key, out var entry))
            {
                entry = (new List<string>(item.FolderPath), new List<PstMailMessage>());
                byKey[key] = entry; order.Add(key);
            }
            entry.Msgs.Add(item.Message);
        }
        var result = new List<PstMailFolder>();
        foreach (string k in order) result.Add(new PstMailFolder(byKey[k].Path, byKey[k].Msgs));
        return result;
    }
}

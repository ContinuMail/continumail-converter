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
/// time, so a large PST is never fully materialized. Productionized from the Integration.Tests PstReader
/// spike. Failure policy: a non-mail folder with messages, or a per-message read failure, is reported via
/// onWarning and skipped; OutOfMemory/process failures and a fatal open/root-walk failure propagate.
/// </summary>
public static class PstMailReader
{
    /// <summary>Lazy: the PST stays open while iterating. Attachment OpenRead must be called during iteration.</summary>
    public static IEnumerable<PstMailItem> EnumerateMessages(string pstPath, Action<string>? onWarning = null)
    {
        var pst = new PSTFile(pstPath, FileAccess.Read);            // fatal on failure -> propagate
        try
        {
            ushort? keywordsId = PropertyNameToIDMap.ResolveStringNamedProperty(pst, 2, "Keywords");
            foreach (PSTFolder child in pst.TopOfPersonalFolders.GetChildFolders())
                foreach (PstMailItem item in EnumerateFolder(child, new List<string>(), keywordsId, onWarning))
                    yield return item;
        }
        finally { pst.CloseFile(); }
    }

    private static IEnumerable<PstMailItem> EnumerateFolder(
        PSTFolder folder, List<string> parentPath, ushort? keywordsId, Action<string>? onWarning)
    {
        var path = new List<string>(parentPath) { folder.DisplayName };

        if (folder is MailFolder mf)
        {
            for (int i = 0; i < mf.MessageCount; i++)
            {
                PstMailMessage? msg = null;
                try { msg = ReadNote(mf.GetNote(i), keywordsId, onWarning); }
                catch (Exception ex) when (ex is not OutOfMemoryException)   // bad message: skip + warn
                {
                    onWarning?.Invoke(
                        $"skipped message {i} in '{string.Join(" / ", path)}': {ex.GetType().Name}: {ex.Message}");
                }
                if (msg is not null)
                    yield return new PstMailItem(path, msg);                  // yield OUTSIDE the try/catch
            }
        }
        else if (folder.MessageCount > 0)                                    // non-mail with messages: warn + skip
        {
            onWarning?.Invoke(
                $"folder '{string.Join(" / ", path)}' has {folder.MessageCount} message(s) but is not a mail " +
                "folder (container class differs); skipping its messages.");
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
            foreach (PstMailItem item in EnumerateFolder(c, path, keywordsId, onWarning))
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
                Length: null));
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
            Attachments: attachments);
    }

    private static PstRecipientKind MapKind(int? raw, Action<string>? onWarning)
    {
        if (!raw.HasValue) return PstRecipientKind.To;
        return (RecipientType)(uint)raw.Value switch
        {
            RecipientType.To => PstRecipientKind.To,
            RecipientType.Cc => PstRecipientKind.Cc,
            RecipientType.Bcc => PstRecipientKind.Bcc,
            var other => Warn(other, onWarning),
        };

        PstRecipientKind Warn(RecipientType other, Action<string>? onWarning)
        {
            onWarning?.Invoke($"unexpected recipient type raw={raw} enum='{other}' -> treated as To");
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

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using PSTFileFormat;

namespace Mail2Pst.Integration.Tests;

public enum RecipientKind { To, Cc, Bcc }

public sealed record ReadRecipient(string Address, RecipientKind Kind);

public sealed record ReadBackMessage(
    string? Subject,
    string? FromAddress,
    IReadOnlyList<ReadRecipient> Recipients,
    DateTimeOffset? Date,
    IReadOnlyList<string> AttachmentNames,
    bool HasNonEmptyBody,
    string? MessageId,
    bool IsRead,
    bool IsReplied,
    bool IsForwarded,
    bool IsFlagged,
    IReadOnlyList<string> Categories);

public sealed record ReadFolder(IReadOnlyList<string> Path, IReadOnlyList<ReadBackMessage> Messages)
{
    public string DisplayPath => string.Join(" / ", Path);
}

/// <summary>
/// Test-support read-back of an engine-written PST. Reads ALL parts of one output
/// group and merges same-path folders (split outputs) into one logical folder.
/// Visible-attachment / inline semantics match the ContinuMail writer, not universal PST.
///
/// Folders are keyed by full path (<c>FolderPathKey.Join(path)</c>) so nested folders with the
/// same leaf name under different parents are never merged incorrectly.
/// </summary>
public static class PstReader
{
    public static IReadOnlyList<ReadFolder> Read(IEnumerable<string> pstPaths)
    {
        var byKey = new Dictionary<string, (List<string> Path, List<ReadBackMessage> Msgs)>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (string path in pstPaths)
        {
            var pst = new PSTFile(path, FileAccess.Read);
            try
            {
                ushort? keywordsId = PSTFileFormat.PropertyNameToIDMap.ResolveStringNamedProperty(pst, 2, "Keywords");
                foreach (PSTFolder folder in pst.TopOfPersonalFolders.GetChildFolders())
                    ReadFolderRecursive(folder, new List<string>(), byKey, order, keywordsId);
            }
            finally { pst.CloseFile(); }
        }

        return order.Select(k => new ReadFolder(byKey[k].Path, byKey[k].Msgs)).ToList();
    }

    private static void ReadFolderRecursive(
        PSTFolder folder, List<string> parentPath,
        Dictionary<string, (List<string> Path, List<ReadBackMessage> Msgs)> byKey, List<string> order,
        ushort? keywordsId)
    {
        var path = new List<string>(parentPath) { folder.DisplayName };
        string key = FolderPathKey.Join(path);

        if (folder is MailFolder mf)
        {
            if (!byKey.TryGetValue(key, out (List<string> Path, List<ReadBackMessage> Msgs) entry))
            {
                entry = (path, new List<ReadBackMessage>());
                byKey[key] = entry;
                order.Add(key);
            }
            for (int i = 0; i < mf.MessageCount; i++)
                entry.Msgs.Add(ReadNote(mf.GetNote(i), keywordsId));
        }
        else if (folder.MessageCount > 0)
        {
            throw new InvalidOperationException(
                $"Folder '{string.Join(" / ", path)}' has {folder.MessageCount} message(s) but is not a MailFolder; " +
                "read-back would silently skip them.");
        }

        foreach (PSTFolder child in folder.GetChildFolders())
            ReadFolderRecursive(child, path, byKey, order, keywordsId);
    }

    private static ReadBackMessage ReadNote(Note note, ushort? keywordsId)
    {
        // Recipients: read EmailAddress via GetRecipient(); read kind via RecipientsTable directly
        // because GetRecipient() does NOT populate RecipientType (always returns default To).
        // Proven by Task 1 spike (ReadBackSpikeTests.cs accessor notes).
        var recipients = new List<ReadRecipient>();
        for (int i = 0; i < note.RecipientCount; i++)
        {
            MessageRecipient r = note.GetRecipient(i);
            int? typeRaw = note.RecipientsTable!.GetInt32Property(i, PropertyID.PidTagRecipientType);
            recipients.Add(new ReadRecipient(r.EmailAddress ?? string.Empty, MapKind(typeRaw)));
        }

        // Attachments: exclude hidden parts AND parts with a Content-ID (CID).
        // ContinuMail writer marks inline CID parts both ways (PidTagAttachmentHidden + PidTagAttachContentId).
        // Excluding on either is defensive against either marker being set alone.
        var attachmentNames = new List<string>();
        for (int i = 0; i < note.AttachmentCount; i++)
        {
            AttachmentObject att = note.GetAttachmentObject(i);
            bool hidden = att.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden) ?? false;
            string? contentId = att.PC.GetStringProperty(PropertyID.PidTagAttachContentId);
            if (hidden || !string.IsNullOrEmpty(contentId)) continue;
            string? name = att.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename)
                           ?? att.PC.GetStringProperty(PropertyID.PidTagDisplayName);
            attachmentNames.Add(name ?? string.Empty);
        }

        byte[]? html = note.PC.GetBytesProperty(PropertyID.PidTagHtml);
        bool hasBody = !string.IsNullOrWhiteSpace(note.Body) || (html is { Length: > 0 });

        // ClientSubmitTime is setter-only on MessageObject — read via PC by property id (Task 1 spike proven).
        DateTimeOffset? date = null;
        DateTime? submit = note.PC.GetDateTimeProperty(PropertyID.PidTagClientSubmitTime);
        if (submit.HasValue)
            date = new DateTimeOffset(DateTime.SpecifyKind(submit.Value, DateTimeKind.Utc));

        // SentRepresentingEmailAddress is setter-only on MessageObject — read via PC by property id (Task 1 spike proven).
        string? fromAddress = note.PC.GetStringProperty(PropertyID.PidTagSentRepresentingEmailAddress);

        int msgFlags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags) ?? 0;
        bool isRead = (msgFlags & 0x0001) != 0;
        bool isFlagged = note.PC.GetInt32Property(PropertyID.PidTagFlagStatus) == 2;
        int? lastVerb = note.PC.GetInt32Property(PropertyID.PidTagLastVerbExecuted);
        bool isReplied = lastVerb is 102 or 103;   // reply / reply-all
        bool isForwarded = lastVerb is 104;          // forward

        // Read "Keywords" named property (MV-Unicode) as categories.
        IReadOnlyList<string> categories = Array.Empty<string>();
        if (keywordsId is ushort kid)
        {
            var rec = note.PC.GetRecordByPropertyID((PSTFileFormat.PropertyID)kid);
            if (rec != null)
                categories = PSTFileFormat.PropertyContext.DeserializeMultiString(note.PC.GetExternalRecordData(rec));
        }

        return new ReadBackMessage(
            Subject: note.Subject,
            FromAddress: fromAddress,
            Recipients: recipients,
            Date: date,
            AttachmentNames: attachmentNames,
            HasNonEmptyBody: hasBody,
            MessageId: note.PC.GetStringProperty(PropertyID.PidTagInternetMessageId),
            IsRead: isRead,
            IsReplied: isReplied,
            IsForwarded: isForwarded,
            IsFlagged: isFlagged,
            Categories: categories);
    }

    private static RecipientKind MapKind(int? raw)
    {
        if (!raw.HasValue)
            throw new InvalidOperationException("recipient has no PidTagRecipientType");
        return (RecipientType)(uint)raw.Value switch
        {
            RecipientType.To => RecipientKind.To,
            RecipientType.Cc => RecipientKind.Cc,
            RecipientType.Bcc => RecipientKind.Bcc,
            var other => throw new InvalidOperationException($"unexpected recipient type: {other}"),
        };
    }
}

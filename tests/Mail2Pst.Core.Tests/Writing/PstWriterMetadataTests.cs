// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterMetadataTests
{

    private const int MsgFlagRead = 0x0001;
    private const int FollowupFlagged = 2;
    private const int FollowupIconRed = 6;
    private const int LastVerbReply = 102;
    private const int LastVerbForward = 104;

    // Writes a single MailMessage into a temp PST and returns the Note for
    // assertion. Caller is responsible for calling pst.CloseFile() and
    // Directory.Delete(tempDir, true) in a finally block.
    private static (Note note, PSTFile pst, string tempDir) WriteAndReadNote(MailMessage message)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var plan = new PstOutputPlan { Name = "Test", MaxSizeBytes = 100L * 1024 * 1024 };
        var planned = new List<PlannedMessage>
        {
            new() { TargetFolderPath = new[] { "Inbox" }, Message = message }
        };

        var writer = new PstWriter();
        List<string> outputFiles = writer.WritePlan(plan, planned, tempDir, new ConversionReport());

        var pst = new PSTFile(outputFiles[0], FileAccess.Read);
        PSTFolder root = pst.TopOfPersonalFolders;
        var inbox = (MailFolder)root.FindChildFolder("Inbox")!;
        Note note = inbox.GetNote(0);

        return (note, pst, tempDir);
    }

    private static MailMessage MinimalMessage() => new()
    {
        Subject = "Test",
        From = new MailAddress { Name = "Alice", Email = "alice@example.com" },
        To = new List<MailAddress> { new() { Email = "bob@example.com" } },
        TextBody = "body",
        Source = new SourceReference { SourcePath = "test.mbox", Identifier = "msg#1" },
    };

    // --- Threading MAPI property tests ---

    [Fact]
    public void Write_MessageIdSet_PidTagInternetMessageIdWritten()
    {
        var message = MinimalMessage();
        message.MessageId = "<msg@example.com>";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("<msg@example.com>", note.PC.GetStringProperty(PropertyID.PidTagInternetMessageId));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_InReplyToSet_PidTagInReplyToIdWritten()
    {
        var message = MinimalMessage();
        message.InReplyTo = "<orig@example.com>";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("<orig@example.com>", note.PC.GetStringProperty((PropertyID)0x1042));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_ReferencesSet_PidTagInternetReferencesWritten()
    {
        var message = MinimalMessage();
        message.References = "<a@x.com> <b@x.com>";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("<a@x.com> <b@x.com>", note.PC.GetStringProperty((PropertyID)0x1039));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Sender (From) tests ---

    [Fact]
    public void Write_FromWithEmptyEmail_DoesNotWriteBogusSmtpSender()
    {
        var message = MinimalMessage();
        // Malformed "From: <>" — empty email, no display name.
        message.From = new MailAddress { Name = null, Email = string.Empty };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            // No crash, and no "SMTP" address type claimed for a non-existent address.
            Assert.True(string.IsNullOrEmpty(note.PC.GetStringProperty(PropertyID.PidTagSenderEmailAddress)));
            Assert.True(string.IsNullOrEmpty(note.PC.GetStringProperty(PropertyID.PidTagSenderAddressType)));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Message flag tests ---

    [Fact]
    public void Write_IsReadTrue_PidTagMessageFlagsHasReadBit()
    {
        var message = MinimalMessage();
        message.IsRead = true;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.NotEqual(0, flags & 0x0001);
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_IsReadFalse_PidTagMessageFlagsLacksReadBit()
    {
        var message = MinimalMessage();
        message.IsRead = false;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.Equal(0, flags & 0x0001);
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_MessageWithAttachment_PidTagMessageFlagsHasAttachBit()
    {
        var message = MinimalMessage();
        message.Attachments = new List<MailAttachment>
        {
            new() { FileName = "a.txt", MimeType = "text/plain", Content = AttachmentContent.FromBytes(new byte[] { 1 }) },
        };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.NotEqual(0, flags & 0x0010);
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_MessageWithoutAttachment_PidTagMessageFlagsLacksAttachBit()
    {
        var message = MinimalMessage();
        // Attachments defaults to empty list

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.Equal(0, flags & 0x0010);
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_IsReadTrueWithAttachment_BothFlagBitsSet()
    {
        var message = MinimalMessage();
        message.IsRead = true;
        message.Attachments = new List<MailAttachment>
        {
            new() { FileName = "b.txt", MimeType = "text/plain", Content = AttachmentContent.FromBytes(new byte[] { 2 }) },
        };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.NotEqual(0, flags & 0x0001); // MSGFLAG_READ
            Assert.NotEqual(0, flags & 0x0010); // MSGFLAG_HASATTACH
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Importance MAPI property tests ---

    [Fact]
    public void Write_ImportanceHigh_PidTagImportanceAndPriorityCorrect()
    {
        var message = MinimalMessage();
        message.Importance = MailImportance.High;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(2, note.PC.GetInt32Property(PropertyID.PidTagImportance));
            Assert.Equal(1, note.PC.GetInt32Property(PropertyID.PidTagPriority));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_ImportanceLow_PidTagImportanceAndPriorityCorrect()
    {
        var message = MinimalMessage();
        message.Importance = MailImportance.Low;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(0, note.PC.GetInt32Property(PropertyID.PidTagImportance));
            Assert.Equal(-1, note.PC.GetInt32Property(PropertyID.PidTagPriority));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_ImportanceNormal_PidTagImportanceAndPriorityCorrect()
    {
        var message = MinimalMessage();
        // Importance defaults to Normal

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(1, note.PC.GetInt32Property(PropertyID.PidTagImportance));
            Assert.Equal(0, note.PC.GetInt32Property(PropertyID.PidTagPriority));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Conversation topic / normalized subject tests ---

    [Fact]
    public void Write_SubjectWithRePrefix_ConversationTopicStripsPrefix()
    {
        var message = MinimalMessage();
        message.Subject = "Re: Hello World";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("Hello World", note.PC.GetStringProperty(PropertyID.PidTagConversationTopic));
            Assert.Equal("Hello World", note.PC.GetStringProperty((PropertyID)0x0E1D));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_SubjectWithFwdAndRePrefix_ConversationTopicStripsAllPrefixes()
    {
        var message = MinimalMessage();
        message.Subject = "Fwd: Re: Hello World";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("Hello World", note.PC.GetStringProperty(PropertyID.PidTagConversationTopic));
            Assert.Equal("Hello World", note.PC.GetStringProperty((PropertyID)0x0E1D));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_SubjectWithMultiplePrefixes_ConversationTopicStripsAllPrefixes()
    {
        var message = MinimalMessage();
        message.Subject = "FWD: FW: Re: Hello World";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("Hello World", note.PC.GetStringProperty(PropertyID.PidTagConversationTopic));
            Assert.Equal("Hello World", note.PC.GetStringProperty((PropertyID)0x0E1D));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_SubjectNoPrefix_ConversationTopicEqualsSubject()
    {
        var message = MinimalMessage();
        message.Subject = "Hello World";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("Hello World", note.PC.GetStringProperty(PropertyID.PidTagConversationTopic));
            Assert.Equal("Hello World", note.PC.GetStringProperty((PropertyID)0x0E1D));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_NullSubject_ConversationTopicIsEmptyString()
    {
        var message = MinimalMessage();
        message.Subject = null;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(string.Empty, note.PC.GetStringProperty(PropertyID.PidTagConversationTopic));
            Assert.Equal(string.Empty, note.PC.GetStringProperty((PropertyID)0x0E1D));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_SubjectWithPaddingWhitespaceAroundPrefix_ConversationTopicStripsCorrectly()
    {
        // Locks down that leading/trailing whitespace and extra space after the colon are trimmed
        var message = MinimalMessage();
        message.Subject = "  RE:   Hello World  ";

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("Hello World", note.PC.GetStringProperty(PropertyID.PidTagConversationTopic));
            Assert.Equal("Hello World", note.PC.GetStringProperty((PropertyID)0x0E1D));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Recipient To/Cc/Bcc type fidelity tests ---

    private static Dictionary<string, int> RecipientTypesByEmail(Note note)
    {
        var result = new Dictionary<string, int>();
        for (int i = 0; i < note.RecipientCount; i++)
        {
            string email = note.RecipientsTable.GetStringProperty(i, PropertyID.PidTagEmailAddress);
            int type = note.RecipientsTable.GetInt32Property(i, PropertyID.PidTagRecipientType)!.Value;
            result[email] = type;
        }
        return result;
    }

    [Fact]
    public void Write_ToCcBcc_EachRecipientGetsCorrectPidTagRecipientType()
    {
        var message = MinimalMessage();
        message.To = new List<MailAddress> { new() { Email = "to@example.com" } };
        message.Cc = new List<MailAddress> { new() { Email = "cc@example.com" } };
        message.Bcc = new List<MailAddress> { new() { Email = "bcc@example.com" } };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Dictionary<string, int> types = RecipientTypesByEmail(note);
            Assert.Equal((int)RecipientType.To, types["to@example.com"]);
            Assert.Equal((int)RecipientType.Cc, types["cc@example.com"]);
            Assert.Equal((int)RecipientType.Bcc, types["bcc@example.com"]);
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_ToCcBcc_DisplayColumnsGroupedByType()
    {
        var message = MinimalMessage();
        message.To = new List<MailAddress> { new() { Name = "Tom", Email = "to@example.com" } };
        message.Cc = new List<MailAddress> { new() { Name = "Carol", Email = "cc@example.com" } };
        message.Bcc = new List<MailAddress> { new() { Name = "Ben", Email = "bcc@example.com" } };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal("Tom", note.PC.GetStringProperty(PropertyID.PidTagDisplayTo));
            Assert.Equal("Carol", note.PC.GetStringProperty(PropertyID.PidTagDisplayCc));
            Assert.Equal("Ben", note.PC.GetStringProperty(PropertyID.PidTagDisplayBcc));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Inline (CID) attachment / phantom-paperclip tests ---

    private static MailAttachment InlineAttachment(string fileName, string contentId) => new()
    {
        FileName = fileName,
        MimeType = "image/png",
        ContentId = contentId,
        IsInline = true,
        Content = AttachmentContent.FromBytes(new byte[] { 1, 2, 3 }),
    };

    private static MailAttachment RealAttachment(string fileName) => new()
    {
        FileName = fileName,
        MimeType = "application/pdf",
        IsInline = false,
        Content = AttachmentContent.FromBytes(new byte[] { 4, 5, 6 }),
    };

    [Fact]
    public void Write_InlineOnlyAttachment_NoHasAttachFlag_AndAttachmentHidden()
    {
        // A message whose only attachments are inline (CID) images is the
        // original mail showing no attachment — it must not get a paperclip.
        var message = MinimalMessage();
        message.Attachments = new List<MailAttachment> { InlineAttachment("logo.png", "logo1") };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.Equal(0, flags & 0x0010); // MSGFLAG_HASATTACH not set
            var att = note.GetAttachmentObject(0);
            Assert.True(att.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden, false));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_MixedInlineAndRealAttachment_HasAttachFlag_OnlyInlineHidden()
    {
        var message = MinimalMessage();
        message.Attachments = new List<MailAttachment>
        {
            InlineAttachment("logo.png", "logo1"),
            RealAttachment("report.pdf"),
        };

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.NotEqual(0, flags & 0x0010); // a real attachment is present -> HASATTACH set

            // Identify by Content-Id rather than index so the test is order-independent.
            for (int i = 0; i < note.AttachmentCount; i++)
            {
                var att = note.GetAttachmentObject(i);
                string cid = att.PC.GetStringProperty(PropertyID.PidTagAttachContentId);
                bool hidden = att.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden, false);
                if (!string.IsNullOrEmpty(cid)) Assert.True(hidden);   // inline -> hidden
                else Assert.False(hidden);                              // real -> visible
            }
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    // --- Follow-up flag and last-verb (X-Mozilla-Status) tests ---

    [Fact]
    public void Write_IsFlagged_FlagStatusAndFollowupIconSet()
    {
        var message = MinimalMessage();
        message.IsFlagged = true;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(FollowupFlagged, note.PC.GetInt32Property(PropertyID.PidTagFlagStatus));
            Assert.Equal(FollowupIconRed, note.PC.GetInt32Property(PropertyID.PidTagFollowupIcon));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_NotFlagged_NoFlagStatus()
    {
        var message = MinimalMessage(); // IsFlagged defaults false

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Null(note.PC.GetInt32Property(PropertyID.PidTagFlagStatus));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_UnreadAndFlagged_ReadBitClear_AndFlagStatusSet()
    {
        // Read and flagged are independent properties: unread + starred must round-trip as both.
        var message = MinimalMessage();
        message.IsRead = false;
        message.IsFlagged = true;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            int flags = note.PC.GetInt32Property(PropertyID.PidTagMessageFlags)!.Value;
            Assert.Equal(0, flags & MsgFlagRead);                                              // MSGFLAG_READ clear
            Assert.Equal(FollowupFlagged, note.PC.GetInt32Property(PropertyID.PidTagFlagStatus)); // still flagged
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_Replied_LastVerbIsReply()
    {
        var message = MinimalMessage();
        message.IsReplied = true;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(LastVerbReply, note.PC.GetInt32Property(PropertyID.PidTagLastVerbExecuted));
            Assert.NotNull(note.PC.GetDateTimeProperty(PropertyID.PidTagLastVerbExecutionTime));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_Forwarded_LastVerbIsForward()
    {
        var message = MinimalMessage();
        message.IsForwarded = true;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(LastVerbForward, note.PC.GetInt32Property(PropertyID.PidTagLastVerbExecuted));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_RepliedAndForwarded_PrefersReplyVerb()
    {
        // Single-valued PidTagLastVerbExecuted -> documented precedence: reply (102) wins.
        var message = MinimalMessage();
        message.IsReplied = true;
        message.IsForwarded = true;

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Equal(LastVerbReply, note.PC.GetInt32Property(PropertyID.PidTagLastVerbExecuted));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void Write_NoVerb_NoLastVerbExecuted()
    {
        var message = MinimalMessage(); // not replied, not forwarded

        var (note, pst, tempDir) = WriteAndReadNote(message);
        try
        {
            Assert.Null(note.PC.GetInt32Property(PropertyID.PidTagLastVerbExecuted));
        }
        finally { pst.CloseFile(); Directory.Delete(tempDir, true); }
    }
}

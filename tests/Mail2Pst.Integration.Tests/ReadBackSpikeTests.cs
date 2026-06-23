// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

// ACCESSOR NOTES (proven during spike, 2026-06-17):
//
// READ-ONLY accessors (properties that are getter+setter or getter-only on MessageObject):
//   note.Subject                   — OK (getter on MessageObject.Properties.cs via PidTagSubject)
//   note.Body                      — OK (getter on MessageObject.Properties.cs via PidTagBody)
//   note.RecipientCount            — OK (getter on MessageObject.cs)
//   note.AttachmentCount           — OK (getter on MessageObject.cs)
//   note.GetRecipient(i)           — OK (MessageObject.cs -> RecipientsTable.GetRecipient)
//   note.GetAttachmentObject(i)    — OK (MessageObject.cs)
//
// SETTER-ONLY on MessageObject — must read via PC.Get*Property(PropertyID) by id:
//   note.SentRepresentingEmailAddress  -> note.PC.GetStringProperty(PropertyID.PidTagSentRepresentingEmailAddress)
//   note.ClientSubmitTime              -> note.PC.GetDateTimeProperty(PropertyID.PidTagClientSubmitTime)
//
// RECIPIENT TYPE gap in RecipientsTable.GetRecipient():
//   GetRecipient() reads PidTagRecipientFlags (meeting flags) but NOT PidTagRecipientType.
//   rcp.RecipientType is always the struct default (RecipientType.To).
//   Read type via: note.RecipientsTable!.GetInt32Property(r, PropertyID.PidTagRecipientType)
//   cast to RecipientType.
//
// ATTACHMENT:
//   att.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden) — OK
//   att.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename) — OK
//   att.PC.GetStringProperty(PropertyID.PidTagDisplayName)        — OK (fallback)

using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class ReadBackSpikeTests
{
    [Fact]
    public void Spike_AllReadBackAccessorsWorkAgainstEngineWrittenPst()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-spike-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        try
        {
            // sample.mbox has 2 messages with From/To/Cc and Message-IDs (known-good recipient coverage).
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig> { new() { Type = "mbox", Path = Path.Combine(fixtures, "sample.mbox") } },
                    },
                },
            };
            var report = new ConversionRunner().Run(config, outDir);
            Assert.NotEmpty(report.OutputFiles);

            bool sawRecipient = false, sawTo = false, sawCc = false, sawMessageId = false, sawDate = false, sawFrom = false, sawBody = false;

            var pst = new PSTFile(report.OutputFiles[0], FileAccess.Read);
            try
            {
                foreach (PSTFolder folder in pst.TopOfPersonalFolders.GetChildFolders())
                {
                    if (folder is not MailFolder mf) continue;
                    Assert.True(mf.MessageCount > 0, "expected at least one message in folder " + folder.DisplayName);
                    for (int i = 0; i < mf.MessageCount; i++)
                    {
                        Note note = mf.GetNote(i);

                        // subject + from + date + body
                        Assert.False(string.IsNullOrEmpty(note.Subject));

                        // SentRepresentingEmailAddress is SETTER-ONLY on MessageObject — read via PC by property id.
                        string? fromAddr = note.PC.GetStringProperty(PropertyID.PidTagSentRepresentingEmailAddress);
                        if (!string.IsNullOrEmpty(fromAddr)) sawFrom = true;

                        // ClientSubmitTime is SETTER-ONLY on MessageObject — read via PC by property id.
                        DateTime? submitTime = note.PC.GetDateTimeProperty(PropertyID.PidTagClientSubmitTime);
                        if (submitTime.HasValue && submitTime.Value != default) sawDate = true;

                        if (!string.IsNullOrWhiteSpace(note.Body)) sawBody = true;

                        // message-id
                        string? mid = note.PC.GetStringProperty(PropertyID.PidTagInternetMessageId);
                        if (!string.IsNullOrEmpty(mid)) sawMessageId = true;

                        // recipients (the gating unknown)
                        // NOTE: GetRecipient() populates EmailAddress and DisplayName from the recipients TC,
                        // but does NOT read PidTagRecipientType — RecipientType is always the struct default (To).
                        // Read the actual type via note.RecipientsTable.GetInt32Property(r, PidTagRecipientType).
                        for (int r = 0; r < note.RecipientCount; r++)
                        {
                            MessageRecipient rcp = note.GetRecipient(r);
                            Assert.False(string.IsNullOrEmpty(rcp.EmailAddress));
                            sawRecipient = true;

                            // Read recipient type directly from the recipients table (proven accessor).
                            int? recipientTypeRaw = note.RecipientsTable!.GetInt32Property(r, PropertyID.PidTagRecipientType);
                            RecipientType recipientType = recipientTypeRaw.HasValue
                                ? (RecipientType)(uint)recipientTypeRaw.Value
                                : RecipientType.To;

                            if (recipientType == RecipientType.To) sawTo = true;
                            if (recipientType == RecipientType.Cc) sawCc = true;
                        }

                        // (attachment accessors are proven in Spike_AttachmentReadBackWorks below,
                        // since sample.mbox has no attachments)
                    }
                }
            }
            finally { pst.CloseFile(); }

            Assert.True(sawFrom, "From (PidTagSentRepresentingEmailAddress) not readable");
            Assert.True(sawDate, "ClientSubmitTime not readable");
            Assert.True(sawBody, "Body not readable");
            Assert.True(sawMessageId, "PidTagInternetMessageId not readable");
            Assert.True(sawRecipient, "recipients not readable");
            Assert.True(sawTo, "To recipient type not readable");
            Assert.True(sawCc, "Cc recipient type not readable");
        }
        finally { Directory.Delete(outDir, true); }
    }

    [Fact]
    public void Spike_AttachmentReadBackWorks()
    {
        // sample.mbox has no attachments, so attachment accessors are proven here against
        // mbox-with-attachments.mbox (which has both real attachments and hidden inline CID images).
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-spike-att-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig> { new() { Type = "mbox", Path = Path.Combine(fixtures, "mbox-with-attachments.mbox") } },
                    },
                },
            };
            var report = new ConversionRunner().Run(config, outDir);

            bool sawVisibleName = false, sawHidden = false;
            var pst = new PSTFile(report.OutputFiles[0], FileAccess.Read);
            try
            {
                foreach (PSTFolder folder in pst.TopOfPersonalFolders.GetChildFolders())
                {
                    if (folder is not MailFolder mf) continue;
                    for (int i = 0; i < mf.MessageCount; i++)
                    {
                        Note note = mf.GetNote(i);
                        for (int a = 0; a < note.AttachmentCount; a++)
                        {
                            AttachmentObject att = note.GetAttachmentObject(a);
                            // att.PC.GetBooleanProperty — proven accessor for hidden flag.
                            bool hidden = att.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden) ?? false;
                            // att.PC.GetStringProperty — proven accessor for filename (long then display fallback).
                            string? name = att.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename)
                                           ?? att.PC.GetStringProperty(PropertyID.PidTagDisplayName);
                            if (hidden) sawHidden = true;
                            else if (!string.IsNullOrEmpty(name)) sawVisibleName = true;
                        }
                    }
                }
            }
            finally { pst.CloseFile(); }

            Assert.True(sawVisibleName, "visible attachment long-filename not readable");
            Assert.True(sawHidden, "hidden (inline) attachment flag not readable");
        }
        finally { Directory.Delete(outDir, true); }
    }
}

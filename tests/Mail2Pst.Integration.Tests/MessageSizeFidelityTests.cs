// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core.Models;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Guards the PidTagMessageSize invariant scanpst enforces: every message's stored size must
/// equal its actual on-disk size (message node data tree + all subnodes). scanpst recomputes
/// this and cross-checks each folder contents-table row against the message sub-object; a
/// stale/legacy size makes EVERY written message report "row doesn't match sub-object"
/// (RepairRequired). Root-caused 2026-07-07: the writer opened parts in Outlook2007RTM mode,
/// selecting the vendored legacy formula (sum of property lengths) instead of on-disk length.
/// </summary>
public class MessageSizeFidelityTests
{
    [Fact]
    public void WrittenMessages_StoreOnDiskMessageSize()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-size-fidelity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var messages = new[]
            {
                Msg("Subject only"),
                Msg("Plaintext", text: "A plain body."),
                Msg("Html", html: "<p>An HTML body.</p>"),
                WithAttachment(Msg("Attachment", text: "See attached.")),
            };
            IReadOnlyList<string> psts = RoundTripHarness.ConvertMessages(messages, outDir);
            Assert.NotEmpty(psts);

            int checked_ = 0;
            foreach (string pstPath in psts)
            {
                var pst = new PSTFile(pstPath, FileAccess.Read);
                try
                {
                    foreach (PSTFolder folder in pst.TopOfPersonalFolders.GetChildFolders())
                        checked_ += AssertFolderMessageSizes(pst, folder);
                }
                finally { pst.CloseFile(); }
            }
            Assert.Equal(messages.Length, checked_);
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static int AssertFolderMessageSizes(PSTFile pst, PSTFolder folder)
    {
        int checked_ = 0;
        var tc = folder.GetContentsTable();
        if (tc is not null)
        {
            for (int i = 0; i < tc.RowCount; i++)
            {
                var message = MessageObject.GetMessage(pst, new NodeID(tc.GetRowID(i)));
                int? stored = message.PC.GetInt32Property(PropertyID.PidTagMessageSize);
                Assert.NotNull(stored);

                int onDisk = message.DataTree.TotalDataLength
                             + (message.SubnodeBTree?.GetDataLengthOfAllSubnodes() ?? 0);
                Assert.True(stored == onDisk,
                    $"message '{message.PC.GetStringProperty(PropertyID.PidTagSubject)}' in folder " +
                    $"'{folder.DisplayName}': stored PidTagMessageSize={stored} but on-disk size={onDisk} " +
                    "(scanpst validates the contents-table row against the on-disk size — a mismatch is RepairRequired)");
                checked_++;
            }
        }

        foreach (PSTFolder child in folder.GetChildFolders())
            checked_ += AssertFolderMessageSizes(pst, child);
        return checked_;
    }

    private static MailMessage Msg(string subject, string? text = null, string? html = null) => new()
    {
        Subject = subject,
        From = new MailAddress { Name = "Sender", Email = "sender@example.com" },
        To = { new MailAddress { Name = "Rcpt", Email = "rcpt@example.com" } },
        Date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        TextBody = text,
        HtmlBody = html,
    };

    private static MailMessage WithAttachment(MailMessage m)
    {
        m.Attachments.Add(new MailAttachment
        {
            FileName = "note.txt",
            MimeType = "text/plain",
            IsInline = false,
            Content = AttachmentContent.FromBytes(Encoding.UTF8.GetBytes("attachment payload")),
        });
        return m;
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class AttachmentWriterTests
{
    [Fact]
    public void ByValue_attachment_round_trips_filename_and_bytes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"attw-{Guid.NewGuid():N}.pst");
        PSTFile? file = null;
        try
        {
            PSTFile.CreateEmptyStore(path);                               // returns void
            try
            {
                file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                file.BeginSavingChanges();
                PSTFolder inbox = file.TopOfPersonalFolders.CreateChildFolder("Inbox", FolderItemTypeName.Note);
                Note note = Note.CreateNewNote(file, inbox.NodeID);
                note.Subject = "carrier";
                new AttachmentWriter().Write(file, note, new AttachmentSpec(
                    "readme.txt", "text/plain", AttachmentContent.FromBytes(new byte[] { 1, 2, 3, 4 })));
                note.SaveChanges();
                inbox.AddMessage(note);
                inbox.SaveChanges();
                file.EndSavingChanges();
            }
            finally { file?.CloseFile(); file = null; }

            try
            {
                file = new PSTFile(path, FileAccess.Read);
                PSTFolder inbox = file.TopOfPersonalFolders.FindChildFolder("Inbox");
                Note note = Note.GetNote(file, inbox.GetMessage(0).NodeID);
                AttachmentObject att = note.GetAttachmentObject(0);
                Assert.Equal("readme.txt", att.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename));
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, att.PC.GetBytesProperty(PropertyID.PidTagAttachData));
            }
            finally { file?.CloseFile(); file = null; }
        }
        finally { File.Delete(path); }
    }
}

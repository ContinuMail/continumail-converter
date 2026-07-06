// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class AssociatedMessageTests
{
    [Fact]
    public void AddAssociatedMessage_writes_into_associated_table_not_regular_contents()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fai-{System.Guid.NewGuid():N}.pst");
        try
        {
            PSTFile.CreateEmptyStore(path);
            var file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            file.BeginSavingChanges();
            PSTFolder calendar = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);

            MessageObject fai = MessageObject.CreateNewMessage(file, FolderItemTypeName.Note, calendar.NodeID);
            fai.PC.SetStringProperty(PropertyID.PidTagMessageClass, "IPM.Configuration.CategoryList");
            fai.PC.SetStringProperty(PropertyID.PidTagSubject, "IPM.Configuration.CategoryList");
            fai.SaveChanges();
            calendar.AddAssociatedMessage(fai);

            file.EndSavingChanges();
            file.CloseFile();

            var reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            PSTFolder cal = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
            Assert.Equal(1, cal.AssociatedMessageCount);        // exactly one FAI in the associated table
            Assert.Equal(0, cal.MessageCount);                  // NOT in the regular contents table
            MessageObject read = cal.GetAssociatedMessage(0);
            Assert.Equal("IPM.Configuration.CategoryList",
                read.PC.GetStringProperty(PropertyID.PidTagMessageClass));
            // The primitive guarantees MSGFLAG_ASSOCIATED (0x40) regardless of the caller.
            Assert.True((read.PC.GetInt32Property(PropertyID.PidTagMessageFlags) & 0x40) == 0x40);
            reopened.CloseFile();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

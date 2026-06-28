// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class ContactMessageTests
{
    [Fact]
    public void CreateNewContact_SetsContactMessageClass()
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-contactmsg-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            pst.BeginSavingChanges();
            PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Contacts", FolderItemTypeName.Contact);
            ContactMessage contact = ContactMessage.CreateNewContact(pst, folder.NodeID);
            Assert.Equal("IPM.Contact", contact.MessageClass);
        }
        finally { pst?.CloseFile(); File.Delete(path); }
    }
}

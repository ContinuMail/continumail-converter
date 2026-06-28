// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class NumericNamedPropertyTests
{
    [Fact]
    public void ObtainIDFromName_PsetidAddressLid_RoundTripsAsNumericNamedProperty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-namedprop-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        var lid = (PropertyLongID)0x8083; // PidLidEmail1EmailAddress
        PSTFile? pst = null;
        try
        {
            // Write (PSTFile is NOT IDisposable — explicit CloseFile in finally)
            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("C", FolderItemTypeName.Contact);
                MessageObject msg = MessageObject.CreateNewMessage(pst, FolderItemTypeName.Contact, folder.NodeID);
                PropertyID id = pst.NameToIDMap.ObtainIDFromName(new PropertyName(lid, PropertySetGuid.PSETID_Address));
                msg.PC.SetStringProperty(id, "alice@example.com");
                msg.SaveChanges();
                folder.AddMessage(msg);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            // Read back
            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PropertyID id = pst.NameToIDMap.ObtainIDFromName(new PropertyName(lid, PropertySetGuid.PSETID_Address));
                PSTFolder folder = pst.TopOfPersonalFolders.FindChildFolder("C");
                MessageObject msg = folder.GetMessage(0); // PSTFolder.GetMessage(int index)
                Assert.Equal("alice@example.com", msg.PC.GetStringProperty(id));
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }
}

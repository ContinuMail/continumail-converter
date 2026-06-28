// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

public class ContactFolderClassTests
{
    [Fact]
    public void CreateChildFolder_ContactItemType_ProducesIpfContactContainerClass()
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-contactfolder-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        PSTFile? pst = null;
        try
        {
            pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            pst.BeginSavingChanges();
            PSTFolder root = pst.TopOfPersonalFolders;
            PSTFolder contacts = root.CreateChildFolder("Contacts", FolderItemTypeName.Contact);
            contacts.SaveChanges();
            pst.EndSavingChanges();

            Assert.Equal("IPF.Contact", contacts.ContainerClass);
        }
        finally
        {
            pst?.CloseFile();
            File.Delete(path);
        }
    }
}

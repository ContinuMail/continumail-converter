// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstPartManagerContactTests
{
    [Fact]
    public void WriteContact_CreatesIpfContactFolder_AndWritesContact()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-ppm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var writer = new ContactWriter();
        try
        {
            var mgr = new PstPartManager("Test", dir, long.MaxValue, 500,
                writeMessage: (f, fo, m) => { },
                writeContact: (f, fo, c) => writer.WriteContact(f, fo, c));
            var contactFolder = new List<string> { "Contacts", "Personal Address Book" };
            // IMPORTANT: pre-create as Contact type, else the leaf is made IPF.Note first and
            // WriteContact would (correctly) throw a collision.
            mgr.Begin(new[] { new FolderToPrecreate(contactFolder, FolderItemTypeName.Contact) });
            mgr.WriteContact(contactFolder, new ContactRecord { DisplayName = "Eve" });
            mgr.OnWritten(256);
            mgr.Finish();
            mgr.Close();

            string pst = Path.Combine(dir, "Test.pst");
            PSTFile? f = null;
            try
            {
                f = new PSTFile(pst, FileAccess.Read);
                PSTFolder contacts = f.TopOfPersonalFolders.FindChildFolder("Contacts").FindChildFolder("Personal Address Book");
                Assert.Equal("IPF.Contact", contacts.ContainerClass);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }
}

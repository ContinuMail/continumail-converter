// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class ContactWriterTests
{
    private static T RoundTrip<T>(ContactRecord record, Func<PSTFile, PSTFolder, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-cw-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        PSTFile? pst = null;
        try
        {
            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Contacts", FolderItemTypeName.Contact);
                new ContactWriter().WriteContact(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PSTFolder folder = pst.TopOfPersonalFolders.FindChildFolder("Contacts");
                return read(pst, folder);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }

    // Helper: PSTFolder has GetMessage(int index) -> MessageObject (NO GetMessageID).
    private static ContactMessage FirstContact(PSTFile pst, PSTFolder folder) =>
        ContactMessage.GetContact(pst, folder.GetMessage(0).NodeID);

    [Fact]
    public void WriteContact_SetsDisplayNameAndMessageClass()
    {
        var record = new ContactRecord { DisplayName = "Alice Smith", GivenName = "Alice", Surname = "Smith" };
        var (cls, name, given) = RoundTrip(record, (pst, folder) =>
        {
            ContactMessage m = FirstContact(pst, folder);
            return (m.MessageClass,
                    m.PC.GetStringProperty(PropertyID.PidTagDisplayName),
                    m.PC.GetStringProperty(PropertyID.PidTagGivenName));
        });
        Assert.Equal("IPM.Contact", cls);
        Assert.Equal("Alice Smith", name);
        Assert.Equal("Alice", given);
    }

    [Fact]
    public void WriteContact_SetsEmail1ViaPsetidAddressNamedProperty()
    {
        var record = new ContactRecord { DisplayName = "Bob", Emails = { "bob@example.com" } };
        string email = RoundTrip(record, (pst, folder) =>
        {
            ContactMessage m = FirstContact(pst, folder);
            PropertyID id = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName((PropertyLongID)0x8083, PropertySetGuid.PSETID_Address));
            return m.PC.GetStringProperty(id);
        });
        Assert.Equal("bob@example.com", email);
    }

    [Fact]
    public void WriteContact_FallsBackDisplayNameFromNameParts()
    {
        var record = new ContactRecord { GivenName = "Carol", Surname = "Jones" };
        string name = RoundTrip(record, (pst, folder) =>
            FirstContact(pst, folder).PC.GetStringProperty(PropertyID.PidTagDisplayName));
        Assert.Equal("Carol Jones", name);
    }

    [Fact]
    public void WriteContact_BirthdayStoredAsCalendarDate_NotShiftedByLocalZone()
    {
        // A birthday given in a +13:00 zone must keep its calendar date.
        var bday = new DateTimeOffset(1990, 5, 20, 0, 0, 0, TimeSpan.FromHours(13));
        var record = new ContactRecord { DisplayName = "Dee", Birthday = bday };
        DateTime stored = RoundTrip(record, (pst, folder) =>
            FirstContact(pst, folder).PC.GetDateTimeProperty(PropertyID.PidTagBirthday).Value);
        Assert.Equal(20, stored.Day);
        Assert.Equal(5, stored.Month);
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class ContactWriterPhotoTests
{
    private static T RoundTrip<T>(ContactRecord rec, Func<PSTFile, PSTFolder, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-cwp-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        PSTFile? pst = null;
        try
        {
            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Contacts", FolderItemTypeName.Contact);
                new ContactWriter().WriteContact(pst, folder, rec);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }
            try { pst = new PSTFile(path, FileAccess.Read); return read(pst, pst.TopOfPersonalFolders.FindChildFolder("Contacts")); }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WriteContact_WithJpegPhoto_WritesFullOutlookContactPhotoRecipe()
    {
        var rec = new ContactRecord { DisplayName = "Pic", Photo = new ContactPhoto { Bytes = new byte[] { 1, 2, 3, 4 }, MediaType = "image/jpeg" } };
        var r = RoundTrip(rec, (pst, folder) =>
        {
            MessageObject m = folder.GetMessage(0);
            AttachmentObject a = m.GetAttachmentObject(0);
            PropertyID hasPicId = pst.NameToIDMap.ObtainIDFromName(new PropertyName((PropertyLongID)0x8015, PropertySetGuid.PSETID_Address));
            return new
            {
                Count = m.AttachmentCount,
                IsPhoto = a.PC.GetBooleanProperty(PropertyID.PidTagAttachmentContactPhoto),
                Hidden = a.PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden),
                Method = a.PC.GetInt32Property(PropertyID.PidTagAttachMethod),
                Data = a.PC.GetBytesProperty(PropertyID.PidTagAttachData),
                LongName = a.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename),
                FileName = a.PC.GetStringProperty(PropertyID.PidTagAttachFilename),
                Display = a.PC.GetStringProperty(PropertyID.PidTagDisplayName),
                Ext = a.PC.GetStringProperty(PropertyID.PidTagAttachExtension),
                Mime = a.PC.GetStringProperty(PropertyID.PidTagAttachMimeTag),
                HasPic = m.PC.GetBooleanProperty(hasPicId),
            };
        });
        Assert.Equal(1, r.Count);
        Assert.True(r.IsPhoto);
        Assert.False(r.Hidden);                       // non-hidden, matches Outlook
        Assert.Equal(1, r.Method);                    // afByValue
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, r.Data);
        Assert.Equal("ContactPicture.jpg", r.LongName);
        Assert.Equal("ContactPicture.jpg", r.FileName);
        Assert.Equal("ContactPicture.jpg", r.Display);
        Assert.Equal(".jpg", r.Ext);
        Assert.Equal("image/jpeg", r.Mime);
        Assert.True(r.HasPic);
    }

    [Fact]
    public void WriteContact_PngPhoto_DerivesPngFilenameAndMime()
    {
        var rec = new ContactRecord { DisplayName = "Png", Photo = new ContactPhoto { Bytes = new byte[] { 9 }, MediaType = "image/png" } };
        var (name, ext, mime) = RoundTrip(rec, (pst, folder) =>
        {
            AttachmentObject a = folder.GetMessage(0).GetAttachmentObject(0);
            return (a.PC.GetStringProperty(PropertyID.PidTagAttachLongFilename),
                    a.PC.GetStringProperty(PropertyID.PidTagAttachExtension),
                    a.PC.GetStringProperty(PropertyID.PidTagAttachMimeTag));
        });
        Assert.Equal("ContactPicture.png", name);
        Assert.Equal(".png", ext);
        Assert.Equal("image/png", mime);
    }

    [Fact]
    public void WriteContact_NoPhoto_WritesNoAttachment()
    {
        int count = RoundTrip(new ContactRecord { DisplayName = "NoPic" }, (pst, folder) => folder.GetMessage(0).AttachmentCount);
        Assert.Equal(0, count);
    }
}

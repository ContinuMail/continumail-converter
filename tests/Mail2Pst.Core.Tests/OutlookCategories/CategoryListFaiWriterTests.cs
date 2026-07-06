// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text;
using Mail2Pst.Core.OutlookCategories;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryListFaiWriterTests
{
    private const int MSGFLAG_ASSOCIATED = 0x40;

    private static string NewStoreWithCalendar(out PSTFolder calendar, out PSTFile file)
    {
        string path = Path.Combine(Path.GetTempPath(), $"faiw-{Guid.NewGuid():N}.pst");
        PSTFile.CreateEmptyStore(path);
        file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        file.BeginSavingChanges();
        calendar = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);
        return path;
    }

    [Fact]
    public void Stamp_writes_one_fai_with_class_subject_flag_and_exact_bytes()
    {
        byte[] xml = Encoding.UTF8.GetBytes("<categories><category name=\"Meeting\" color=\"4\"/></categories>");
        string path = NewStoreWithCalendar(out PSTFolder calendar, out PSTFile file);
        try
        {
            CategoryListFaiWriter.Stamp(file, calendar, xml);
            file.EndSavingChanges();
            file.CloseFile();

            var reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            PSTFolder cal = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
            Assert.Equal(1, cal.AssociatedMessageCount);
            Assert.Equal(0, cal.MessageCount);
            MessageObject fai = cal.GetAssociatedMessage(0);
            Assert.Equal("IPM.Configuration.CategoryList", fai.PC.GetStringProperty(PropertyID.PidTagMessageClass));
            Assert.Equal("IPM.Configuration.CategoryList", fai.PC.GetStringProperty(PropertyID.PidTagSubject));
            Assert.Equal("IPM.Configuration.CategoryList", fai.PC.GetStringProperty((PropertyID)0x0E1D)); // PidTagNormalizedSubject
            Assert.True((fai.PC.GetInt32Property(PropertyID.PidTagMessageFlags) & MSGFLAG_ASSOCIATED) == MSGFLAG_ASSOCIATED);
            Assert.Equal(xml, fai.PC.GetBytesProperty(PropertyID.PidTagRoamingXmlStream)); // byte-for-byte
            reopened.CloseFile();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Stamp_twice_upserts_updates_stream_no_duplicate()
    {
        byte[] first = Encoding.UTF8.GetBytes("<categories><category name=\"Meeting\" color=\"4\"/></categories>");
        byte[] second = Encoding.UTF8.GetBytes("<categories><category name=\"Suppliers\" color=\"9\"/></categories>");
        string path = NewStoreWithCalendar(out PSTFolder calendar, out PSTFile file);
        try
        {
            CategoryListFaiWriter.Stamp(file, calendar, first);
            CategoryListFaiWriter.Stamp(file, calendar, second);   // same folder, second stamp
            file.EndSavingChanges();
            file.CloseFile();

            var reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            PSTFolder cal = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
            Assert.Equal(1, cal.AssociatedMessageCount);           // still exactly one
            Assert.Equal(second, cal.GetAssociatedMessage(0).PC.GetBytesProperty(PropertyID.PidTagRoamingXmlStream));
            reopened.CloseFile();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

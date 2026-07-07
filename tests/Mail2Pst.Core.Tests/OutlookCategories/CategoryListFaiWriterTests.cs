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

    /// <summary>
    /// scanpst requires an associated (FAI) message's node ID to have NID_TYPE_ASSOC_MESSAGE
    /// (0x08), not NID_TYPE_NORMAL_MESSAGE (0x04). With the wrong type it reports
    /// "Associated Contents Table has a bad RowID", ejects the FAI, and orphan-recovers it as a
    /// NORMAL message ("Message flags shouldn't have MSGFLAG_ASSOCIATED") — RepairRequired
    /// (2026-07-07 scanpst root-cause, real-corpus follow-up).
    /// </summary>
    [Fact]
    public void Stamp_fai_node_has_assoc_message_nid_type()
    {
        byte[] xml = Encoding.UTF8.GetBytes("<categories><category name=\"Meeting\" color=\"4\"/></categories>");
        string path = NewStoreWithCalendar(out PSTFolder calendar, out PSTFile file);
        try
        {
            CategoryListFaiWriter.Stamp(file, calendar, xml);
            file.EndSavingChanges();
            file.CloseFile();

            var reopened = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                PSTFolder cal = reopened.TopOfPersonalFolders.FindChildFolder("Calendar");
                MessageObject fai = cal.GetAssociatedMessage(0);
                Assert.True(fai.NodeID.nidType == NodeTypeName.NID_TYPE_ASSOC_MESSAGE,
                    $"FAI node type is {fai.NodeID.nidType} (nid=0x{fai.NodeID.Value:X}) but an associated " +
                    "message must be NID_TYPE_ASSOC_MESSAGE — scanpst ejects it from the associated " +
                    "contents table otherwise ('bad RowID' + orphan recovery)");
            }
            finally { reopened.CloseFile(); }
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

    // Owner kill-gate artifact: writes a never-default PST containing ONE mail item categorized
    // "Meeting" plus a baked CategoryList FAI (Meeting = yellow, OlCategoryColor 5 -> xml color 4).
    // Skipped by default; run explicitly to regenerate the artifact for the manual render gate.
    [Fact(Skip = "Owner kill-gate artifact generator; run manually")]
    public void GENERATE_gate_pst()
    {
        string dir = Path.Combine(Path.GetTempPath(), "continumail-gate");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "category-colour-gate.pst");
        PSTFile.CreateEmptyStore(path);
        var file = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        file.BeginSavingChanges();

        // A mail folder + one message categorized "Meeting".
        PSTFolder inbox = file.TopOfPersonalFolders.CreateChildFolder("Inbox", FolderItemTypeName.Note);
        Note note = Note.CreateNewNote(file, inbox.NodeID);
        note.Subject = "Gate item";
        ushort keywordsId = PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords");
        note.PC.SetMultiStringProperty((PropertyID)keywordsId, new System.Collections.Generic.List<string> { "Meeting" });
        note.SaveChanges();
        inbox.AddMessage(note);
        inbox.SaveChanges();

        // The FAI in a Calendar folder — Meeting = yellow (OlCategoryColor 5).
        PSTFolder calendar = file.TopOfPersonalFolders.CreateChildFolder("Calendar", FolderItemTypeName.Appointment);
        string xml = Mail2Pst.Core.OutlookCategories.CategoryListXml.Append(
            string.Empty, new[] { ("Meeting", 5) });
        CategoryListFaiWriter.Stamp(file, calendar, Encoding.UTF8.GetBytes(xml));

        file.EndSavingChanges();
        file.CloseFile();
        System.Console.WriteLine($"Gate PST: {path}");
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// TDD round-trip tests for attachment emission from <see cref="AppointmentWriter"/>.
/// InlineBytes → visible ByValue PST attachment; LinkOnly → body appendix, no embedded row.
/// </summary>
public class AppointmentWriterAttachmentTests
{
    private static readonly DateTime S = new(2026, 7, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime E = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    // -----------------------------------------------------------------------
    // Round-trip infrastructure (mirrors AppointmentWriterTests.RoundTripAppointment)
    // -----------------------------------------------------------------------

    private static T RoundTripAppointment<T>(AppointmentRecord record, Func<PSTFile, Appointment, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-awatt-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder(
                    "Calendar", FolderItemTypeName.Appointment);
                new AppointmentWriter().WriteAppointment(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
                PSTFolder found = pst.TopOfPersonalFolders.FindChildFolder("Calendar");
                CalendarFolder cal = Assert.IsType<CalendarFolder>(found);
                Assert.Equal(1, cal.AppointmentCount);
                Appointment appt = cal.GetAppointment(0);
                return read(pst, appt);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Inline_attachment_is_written_by_value_and_visible()
    {
        var rec = new AppointmentRecord
        {
            Subject = "s", StartUtc = S, EndUtc = E,
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes,
                    "n.txt", "text/plain", new byte[] { 1, 2, 3 }, null, null)
            }
        };

        var (count, bytes, hidden) = RoundTripAppointment(rec, (f, a) => (
            a.AttachmentCount,
            a.GetAttachmentObject(0).PC.GetBytesProperty(PropertyID.PidTagAttachData),
            a.GetAttachmentObject(0).PC.GetBooleanProperty(PropertyID.PidTagAttachmentHidden)));

        Assert.Equal(1, count);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        Assert.NotEqual(true, hidden);   // calendar InlineBytes is a VISIBLE attachment, not a hidden CID
    }

    [Fact]
    public void Link_only_attachment_is_appended_to_body_not_embedded()
    {
        var rec = new AppointmentRecord
        {
            Subject = "s", StartUtc = S, EndUtc = E,
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.LinkOnly,
                    "doc.pdf", "application/pdf", null, null, "https://x.example.com/doc.pdf")
            }
        };

        var (count, body) = RoundTripAppointment(rec, (f, a) => (
            a.AttachmentCount,
            a.PC.GetStringProperty(PropertyID.PidTagBody) ?? ""));

        Assert.Equal(0, count);                                      // no embedded row for a link
        Assert.Contains("https://x.example.com/doc.pdf", body);     // preserved in body
    }
}

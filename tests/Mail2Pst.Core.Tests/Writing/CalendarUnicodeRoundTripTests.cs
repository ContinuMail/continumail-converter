// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Text;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;
// Disambiguates our model enum from PSTFileFormat.RecurrenceFrequency (the vendor blob type).
using RecurrenceFrequency = Mail2Pst.Core.Models.RecurrenceFrequency;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// Unicode round-trip regression LOCKS for the calendar/task attachment + body pipeline.
/// Each test writes a real <see cref="AppointmentRecord"/> or <see cref="TaskRecord"/>,
/// closes the PST, reopens it, and reads back a specific MAPI property.
///
/// These are LOCK tests: the behavior already exists (Tasks 1–5 landed it); they exist to
/// PIN it so a future encoding regression fails loudly.
/// If a lock test fails it is a REAL encoding bug — fix the writer/resolver/mapper, NOT the test.
/// </summary>
public class CalendarUnicodeRoundTripTests
{
    // -----------------------------------------------------------------------
    // Round-trip infrastructure
    // -----------------------------------------------------------------------

    private static readonly DateTime S = new(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime E = new(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Write one appointment, close, reopen, invoke <paramref name="read"/> (while the file is
    /// still open — required for lazy-loaded subnodes such as attachments on RecurringAppointment),
    /// close, delete. Handles teardown even when assertions throw.
    /// </summary>
    private static T RoundTripAppointment<T>(AppointmentRecord record, Func<PSTFile, Appointment, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-uni-appt-{Guid.NewGuid():N}.pst");
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

    /// <summary>
    /// Write one task, close, reopen, invoke <paramref name="read"/> (file still open),
    /// close, delete.
    /// </summary>
    private static T RoundTripTask<T>(TaskRecord record, Func<PSTFile, MessageObject, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-uni-task-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder("Tasks", FolderItemTypeName.Task);
                new TaskWriter().WriteTask(pst, folder, record);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PSTFolder readFolder = pst.TopOfPersonalFolders.FindChildFolder("Tasks");
                MessageObject msg = TaskMessage.GetTask(pst, readFolder.GetMessage(0).NodeID);
                return read(pst, msg);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Lock 1: InlineBytes attachment filename æøå + emoji — event
    // -----------------------------------------------------------------------

    /// <summary>
    /// "vedlegg-æøå-🦷.txt" in an <see cref="CalendarAttachmentKind.InlineBytes"/> attachment
    /// on an appointment must survive <see cref="AppointmentWriter"/> → PidTagAttachLongFilename
    /// unchanged. Pins UTF-16 emoji round-trip through the vendored PST store.
    /// </summary>
    [Fact]
    public void Attachment_filename_unicode_round_trips_event()
    {
        const string FileName = "vedlegg-æøå-🦷.txt";
        var rec = new AppointmentRecord
        {
            Subject = "Unicode attachment — event",
            StartUtc = S, EndUtc = E,
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes,
                    FileName, "text/plain", new byte[] { 1, 2, 3 }, null, null)
            }
        };

        string? name = RoundTripAppointment(rec,
            (_, appt) => appt.GetAttachmentObject(0).PC.GetStringProperty(PropertyID.PidTagAttachLongFilename));

        Assert.Equal(FileName, name);
    }

    // -----------------------------------------------------------------------
    // Lock 2: InlineBytes attachment filename æøå + emoji — task
    // -----------------------------------------------------------------------

    /// <summary>
    /// Same filename on an IPM.Task via <see cref="TaskWriter"/>.
    /// Pins the shared <see cref="AttachmentWriter"/> path for the task attachment pipeline.
    /// </summary>
    [Fact]
    public void Attachment_filename_unicode_round_trips_task()
    {
        const string FileName = "vedlegg-æøå-🦷.txt";
        var rec = new TaskRecord
        {
            Subject = "Unicode attachment — task",
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes,
                    FileName, "text/plain", new byte[] { 4, 5, 6 }, null, null)
            }
        };

        string? name = RoundTripTask(rec,
            (_, msg) => msg.GetAttachmentObject(0).PC.GetStringProperty(PropertyID.PidTagAttachLongFilename));

        Assert.Equal(FileName, name);
    }

    // -----------------------------------------------------------------------
    // Lock 3: HTML body + inline attachment + Teams URL all coexist
    // -----------------------------------------------------------------------

    /// <summary>
    /// An appointment with <see cref="AppointmentRecord.BodyHtml"/> containing a Teams join URL
    /// AND an <see cref="CalendarAttachmentKind.InlineBytes"/> attachment must survive all three
    /// together in the reopened PST:
    /// <list type="bullet">
    ///   <item>PidTagHtml preserved (UTF-8 bytes, Teams URL intact)</item>
    ///   <item>PidTagNativeBody = 3 (HTML)</item>
    ///   <item>AttachmentCount = 1 and filename preserved</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Body_html_attachment_and_online_meeting_link_coexist()
    {
        const string JoinUrl  = "https://teams.example.com/l/meeting-unicode-lock-test";
        const string FileName = "notes-møte.txt";
        string html = $"<html><body><p>Bli med: <a href=\"{JoinUrl}\">{JoinUrl}</a></p></body></html>";

        var rec = new AppointmentRecord
        {
            Subject  = "HTML + attach + Teams URL",
            StartUtc = S, EndUtc = E,
            BodyHtml = html,
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes,
                    FileName, "text/plain", new byte[] { 7, 8, 9 }, null, null)
            }
        };

        RoundTripAppointment(rec, (_, appt) =>
        {
            byte[]? htmlBytes = appt.PC.GetBytesProperty(PropertyID.PidTagHtml);
            Assert.NotNull(htmlBytes);
            Assert.Contains(JoinUrl, Encoding.UTF8.GetString(htmlBytes!));
            Assert.Equal(3, appt.PC.GetInt32Property(PropertyID.PidTagNativeBody));
            Assert.Equal(1, appt.AttachmentCount);
            Assert.Equal(FileName,
                appt.GetAttachmentObject(0).PC.GetStringProperty(PropertyID.PidTagAttachLongFilename));
            return true;
        });
    }

    // -----------------------------------------------------------------------
    // Lock 4: Recurring override subject with Unicode survives embedded attachment
    // -----------------------------------------------------------------------

    /// <summary>
    /// An override occurrence with subject "Møte 🌍" must survive the <c>method=5</c>
    /// embedded-attachment path in <see cref="AppointmentWriter.ApplyExceptions"/>.
    /// The subject is read back via <see cref="RecurringAppointment.GetModifiedInstance"/> while
    /// the PST file is still open (lazy-load gotcha — subnodes require an open file handle).
    /// Pins the PR7a exception/embedded-attachment Unicode path.
    /// </summary>
    [Fact]
    public void Recurring_override_subject_unicode()
    {
        const string UnicodeSubject = "Møte 🌍";

        var rec = new AppointmentRecord
        {
            Subject               = "Weekly meeting",
            StartUtc              = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            EndUtc                = new DateTime(2026, 7, 1, 1, 30, 0, DateTimeKind.Utc),
            TimeZone              = TimeZoneInfo.Utc,
            OriginatingTimeZoneId = "UTC",
            Recurrence = new RecurrenceSpec
            {
                Frequency             = RecurrenceFrequency.Weekly,
                Interval              = 1,
                DaysOfWeek            = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday },
                EndKind               = RecurrenceEndKind.Count,
                Count                 = 6,
                FirstStartUtc         = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
                FirstStartLocal       = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
                LastInstanceStartUtc  = new DateTime(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc),
                TimeZone              = TimeZoneInfo.Utc,
                OriginatingTimeZoneId = "UTC",
            },
            Exceptions = new[]
            {
                new AppointmentException
                {
                    OriginalInstance = new RecurrenceInstanceId(
                        new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 7, 8, 1, 0, 0, DateTimeKind.Unspecified),
                        "UTC", false),
                    NewStartUtc = new DateTime(2026, 7, 8, 7, 0, 0, DateTimeKind.Utc),
                    NewEndUtc   = new DateTime(2026, 7, 8, 7, 30, 0, DateTimeKind.Utc),
                    Subject     = UnicodeSubject,
                    ChangeFlags = AppointmentExceptionChangeFlags.Subject | AppointmentExceptionChangeFlags.StartEnd,
                }
            }
        };

        // Read the embedded attachment's Subject while the file is open (GetModifiedInstance accesses
        // the subnode tree — requires an open file handle, same pattern as AppointmentWriterRecurrenceTests).
        string? attachSubject = null;
        RoundTripAppointment(rec, (_, appt) =>
        {
            if (appt is RecurringAppointment ra && ra.AttachmentCount > 0)
                attachSubject = ra.GetModifiedInstance(0).Subject;
            return true;
        });

        Assert.Equal(UnicodeSubject, attachSubject);
    }

    // -----------------------------------------------------------------------
    // Lock 5: Inline attachment ContentId with non-ASCII (shared AttachmentWriter)
    // -----------------------------------------------------------------------

    /// <summary>
    /// A non-ASCII <see cref="AttachmentSpec.ContentId"/> written via the shared
    /// <see cref="AttachmentWriter"/> (mail Note path) must survive into
    /// <c>PidTagAttachContentId</c> unchanged. Pins the CID Unicode path through the
    /// vendored PST store — exercising the byte that <see cref="AppointmentWriter"/> and
    /// <see cref="TaskWriter"/> ultimately share for inline attachments.
    /// </summary>
    [Fact]
    public void Inline_attachment_content_id_unicode()
    {
        const string Cid = "cid-æøå@example.com";

        string path = Path.Combine(Path.GetTempPath(), $"m2p-uni-cid-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder inbox = pst.TopOfPersonalFolders.CreateChildFolder("Inbox", FolderItemTypeName.Note);
                Note note = Note.CreateNewNote(pst, inbox.NodeID);
                note.Subject = "CID carrier";
                new AttachmentWriter().Write(pst, note, new AttachmentSpec(
                    "image.png", "image/png",
                    AttachmentContent.FromBytes(new byte[] { 1, 2, 3 }),
                    ContentId: Cid, IsInline: true));
                note.SaveChanges();
                inbox.AddMessage(note);
                inbox.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read);
                PSTFolder inbox = pst.TopOfPersonalFolders.FindChildFolder("Inbox");
                Note note = Note.GetNote(pst, inbox.GetMessage(0).NodeID);
                AttachmentObject att = note.GetAttachmentObject(0);
                string? cid = att.PC.GetStringProperty(PropertyID.PidTagAttachContentId);
                Assert.Equal(Cid, cid);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // -----------------------------------------------------------------------
    // Lock 6: LinkOnly attachment with æøå in URL preserved in body — event
    // -----------------------------------------------------------------------

    /// <summary>
    /// A <see cref="CalendarAttachmentKind.LinkOnly"/> attachment whose
    /// <see cref="CalendarAttachment.PreservedReference"/> contains æøå must appear in the
    /// plain-text body via <see cref="CalendarBodyAppendix"/>. No PST attachment row is created.
    /// </summary>
    [Fact]
    public void Remote_url_title_unicode_preserved_in_body()
    {
        const string Url = "https://ex.example.com/vedlegg-æøå";
        var rec = new AppointmentRecord
        {
            Subject = "Link with Unicode URL",
            StartUtc = S, EndUtc = E,
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.LinkOnly,
                    "vedlegg-æøå.pdf", "application/pdf", null, null, Url)
            }
        };

        var (count, body) = RoundTripAppointment(rec, (_, appt) => (
            appt.AttachmentCount,
            appt.PC.GetStringProperty(PropertyID.PidTagBody) ?? ""));

        Assert.Equal(0, count);     // no embedded PST attachment row for a LinkOnly
        Assert.Contains(Url, body); // URL appears in the CalendarBodyAppendix output
    }

    // -----------------------------------------------------------------------
    // Lock 7: Online-meeting body fallback with Unicode surrounding text
    // -----------------------------------------------------------------------

    /// <summary>
    /// A <see cref="AppointmentRecord.Body"/> containing Unicode text (including emoji) surrounding
    /// an online-meeting URL must survive round-trip into PidTagBody intact.
    /// Pins the UTF-16 plain-text body path through <see cref="AppointmentWriter.WriteBody"/>.
    /// </summary>
    [Fact]
    public void Online_meeting_body_fallback_unicode_surrounding_text()
    {
        const string MeetingUrl  = "https://teams.example.com/l/meet-unicode-lock-test";
        const string UnicodeBody = "Bli med 👉 " + MeetingUrl + "\n\nDenne møtelenken er viktig 🌍";

        var rec = new AppointmentRecord
        {
            Subject  = "Online meeting with Unicode body",
            StartUtc = S, EndUtc = E,
            Body     = UnicodeBody,
        };

        string? body = RoundTripAppointment(rec,
            (_, appt) => appt.PC.GetStringProperty(PropertyID.PidTagBody));

        Assert.NotNull(body);
        Assert.Contains("Bli med 👉", body);
        Assert.Contains(MeetingUrl, body);
        Assert.Contains("🌍", body);
    }

    // -----------------------------------------------------------------------
    // Lock 8a: Relation UID with non-ASCII preserved — event
    // -----------------------------------------------------------------------

    /// <summary>
    /// A non-ASCII relation UID in <see cref="AppointmentRecord.Relations"/> must appear in
    /// the plain-text body via <see cref="CalendarBodyAppendix"/> after
    /// <see cref="AppointmentWriter.WriteBody"/>.
    /// Pins the relation → body-appendix → PidTagBody pipeline for appointments.
    /// </summary>
    [Fact]
    public void Relation_uid_non_ascii_preserved_event()
    {
        const string RelUid = "uid-æøå";
        var rec = new AppointmentRecord
        {
            Subject   = "Event with Unicode relation",
            StartUtc  = S, EndUtc = E,
            Relations = new[] { RelUid },
        };

        string? body = RoundTripAppointment(rec,
            (_, appt) => appt.PC.GetStringProperty(PropertyID.PidTagBody));

        Assert.NotNull(body);
        Assert.Contains(RelUid, body);
    }

    // -----------------------------------------------------------------------
    // Lock 8b: Relation UID with non-ASCII preserved — task
    // -----------------------------------------------------------------------

    /// <summary>
    /// Same relation-UID path via <see cref="TaskWriter"/> / <see cref="CalendarBodyAppendix"/>.
    /// Pins the relation → body-appendix → PidTagBody pipeline for tasks (RawTodo.Relations path).
    /// </summary>
    [Fact]
    public void Relation_uid_non_ascii_preserved_task()
    {
        const string RelUid = "uid-æøå";
        var rec = new TaskRecord
        {
            Subject   = "Task with Unicode relation",
            Relations = new[] { RelUid },
        };

        string? body = RoundTripTask(rec,
            (_, msg) => msg.PC.GetStringProperty(PropertyID.PidTagBody));

        Assert.NotNull(body);
        Assert.Contains(RelUid, body);
    }
}

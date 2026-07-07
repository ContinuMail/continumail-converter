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
/// Guards the recurrence-exception (moved instance) size invariants scanpst enforces
/// (2026-07-07 scanpst root-cause, real-corpus follow-up): the exception is stored as an
/// embedded attachment whose PtypObject record must carry the sub-object's true on-disk
/// size, and whose PidTagAttachSize must be the attachment object size (PC properties +
/// sub-object size). The vendored pre-Outlook2007SP2 formula undercounts both, yielding
/// scanpst "sub-object with invalid size (computed=X, actual=Y)" + "missing or invalid
/// PR_ATTACH_SIZE" + "Attachment table row doesn't match sub-object" (RepairRequired).
/// </summary>
public class ExceptionInstanceSizeFidelityTests
{
    [Fact]
    public void MovedInstance_ExceptionAttachment_StoresOnDiskSubObjectSize()
    {
        var rec = new AppointmentRecord
        {
            Subject = "Standup",
            StartUtc = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 1, 7, 30, 0, DateTimeKind.Utc),
            TimeZone = TimeZoneInfo.Utc,
            OriginatingTimeZoneId = "UTC",
            Recurrence = new RecurrenceSpec
            {
                Frequency = Mail2Pst.Core.Models.RecurrenceFrequency.Weekly,
                Interval = 1,
                DaysOfWeek = new[] { DayOfWeek.Wednesday },
                EndKind = RecurrenceEndKind.Count,
                Count = 6,
                LastInstanceStartUtc = new DateTime(2026, 8, 5, 7, 0, 0, DateTimeKind.Utc),
                FirstStartUtc = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc),
                FirstStartLocal = new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc),
                TimeZone = TimeZoneInfo.Utc,
                OriginatingTimeZoneId = "UTC",
            },
            Exceptions = new[]
            {
                new AppointmentException
                {
                    OriginalInstance = new RecurrenceInstanceId(
                        new DateTime(2026, 7, 8, 7, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 7, 8, 7, 0, 0, DateTimeKind.Unspecified), "UTC", false),
                    NewStartUtc = new DateTime(2026, 7, 8, 9, 0, 0, DateTimeKind.Utc),
                    NewEndUtc = new DateTime(2026, 7, 8, 9, 30, 0, DateTimeKind.Utc),
                    Subject = "Standup MOVED",
                    ChangeFlags = AppointmentExceptionChangeFlags.Subject | AppointmentExceptionChangeFlags.StartEnd,
                },
            },
        };

        string path = Path.Combine(Path.GetTempPath(), $"m2p-excsize-{Guid.NewGuid():N}.pst");
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
                new AppointmentWriter().WriteAppointment(pst, folder, rec);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
                var cal = (CalendarFolder)pst.TopOfPersonalFolders.FindChildFolder("Calendar");
                Appointment appt = cal.GetAppointment(0);
                Assert.Equal(1, appt.AttachmentCount);

                AttachmentObject att = appt.GetAttachmentObject(0);

                // The exception sub-object's true on-disk size.
                Subnode subObject = att.PC.GetObjectProperty(PropertyID.PidTagAttachData);
                Assert.NotNull(subObject);
                int onDiskSubObjectSize = subObject.DataTree?.TotalDataLength ?? 0;
                Assert.True(onDiskSubObjectSize > 0, "exception sub-object has no data");

                // scanpst: "Object has sub-object with invalid size (computed=onDisk, actual=stored)".
                PtypObjectRecord objRecord = att.PC.GetObjectRecordProperty(PropertyID.PidTagAttachData);
                Assert.True(objRecord.ulSize == (uint)onDiskSubObjectSize,
                    $"PtypObject record size={objRecord.ulSize} but sub-object on-disk size={onDiskSubObjectSize} " +
                    "(scanpst: 'sub-object with invalid size')");

                // scanpst: "missing or invalid PR_ATTACH_SIZE" — attachment object size =
                // PC property length (PtypObject record data excluded by the vendored measure)
                // + the sub-object's size.
                int? attachSize = att.PC.GetInt32Property(PropertyID.PidTagAttachSize);
                int expected = att.PC.GetTotalLengthOfAllProperties() + onDiskSubObjectSize;
                Assert.True(attachSize == expected,
                    $"PidTagAttachSize={attachSize} but attachment object size={expected} " +
                    "(scanpst: 'missing or invalid PR_ATTACH_SIZE')");
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

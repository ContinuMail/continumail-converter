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
/// TDD round-trip tests for GlobalObjectId / CleanGlobalObjectId written by <see cref="AppointmentWriter"/>
/// (PidLidGlobalObjectId LID 0x0003 and PidLidCleanGlobalObjectId LID 0x0023, both PSETID_Meeting).
/// </summary>
public class AppointmentWriterGoidTests
{
    // 112-char Exchange GOID hex (bytes 16-19 are 00 so full == clean for this sample).
    private const string GoidHex =
        "040000008200E00074C5B7101A82E00800000000B40F44F359B6DC01000000000000000010000000DC21992D2F90224BB5EE6F8189627094";

    // -----------------------------------------------------------------------
    // Round-trip infrastructure (mirrors AppointmentWriterTests.RoundTripAppointment)
    // -----------------------------------------------------------------------

    private static T RoundTripAppointment<T>(AppointmentRecord record, Func<PSTFile, Appointment, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-awgoid-{Guid.NewGuid():N}.pst");
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
    public void Goid_written_round_trips_both_props()
    {
        // GlobalObjectId set (Exchange-cached event) → PidLidGlobalObjectId == raw bytes,
        // PidLidCleanGlobalObjectId == date-zeroed clone (bytes 16-19 == 0).
        byte[] goidBytes = Convert.FromHexString(GoidHex);

        var a = new AppointmentRecord
        {
            Subject        = "Exchange meeting",
            StartUtc       = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            EndUtc         = new DateTime(2026, 8, 1, 11, 0, 0, DateTimeKind.Utc),
            GlobalObjectId = goidBytes,
        };

        RoundTripAppointment(a, (pst, appt) =>
        {
            // PidLidGlobalObjectId (PSETID_Meeting 0x0003) — use ObtainIDFromName on a reopen (prop was written).
            PropertyID goidId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidGlobalObjectId, PropertySetGuid.PSETID_Meeting));
            byte[]? readGoid = appt.PC.GetBytesProperty(goidId);
            Assert.NotNull(readGoid);
            Assert.Equal(goidBytes, readGoid);

            // PidLidCleanGlobalObjectId (PSETID_Meeting 0x0023)
            PropertyID cleanId = pst.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidCleanGlobalObjectId, PropertySetGuid.PSETID_Meeting));
            byte[]? readClean = appt.PC.GetBytesProperty(cleanId);
            Assert.NotNull(readClean);
            Assert.Equal(56, readClean!.Length);

            // CleanGlobalObjectId: bytes [16..20) must be zero (exception-date zeroed).
            for (int i = 16; i < 20; i++)
                Assert.Equal(0, readClean[i]);

            // Prefix [0..16) and tail [20..56) must match the original GOID.
            for (int i = 0; i < 16; i++)
                Assert.Equal(readGoid![i], readClean[i]);
            for (int i = 20; i < 56; i++)
                Assert.Equal(readGoid![i], readClean[i]);

            return true;
        });
    }

    [Fact]
    public void No_goid_writes_neither_prop()
    {
        // GlobalObjectId == null (Mozilla UUID / CalDAV event) → both props ABSENT.
        // CRITICAL: check absence with GetIDFromName (non-mutating) — NOT ObtainIDFromName,
        // which CREATES the mapping and would make the test lie.
        var a = new AppointmentRecord
        {
            Subject        = "Local event",
            StartUtc       = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            EndUtc         = new DateTime(2026, 8, 1, 11, 0, 0, DateTimeKind.Utc),
            GlobalObjectId = null,
        };

        RoundTripAppointment(a, (pst, appt) =>
        {
            // Non-mutating lookup: returns null if the name was never registered.
            PropertyID? goidId = pst.NameToIDMap.GetIDFromName(
                new PropertyName(PropertyLongID.PidLidGlobalObjectId, PropertySetGuid.PSETID_Meeting));
            PropertyID? cleanId = pst.NameToIDMap.GetIDFromName(
                new PropertyName(PropertyLongID.PidLidCleanGlobalObjectId, PropertySetGuid.PSETID_Meeting));

            // Absent = name not registered at all, OR registered but no value on this message.
            bool goidAbsent  = goidId  is null || appt.PC.GetBytesProperty(goidId.Value)  is null;
            bool cleanAbsent = cleanId is null || appt.PC.GetBytesProperty(cleanId.Value) is null;

            Assert.True(goidAbsent,  "PidLidGlobalObjectId must be absent when GlobalObjectId is null");
            Assert.True(cleanAbsent, "PidLidCleanGlobalObjectId must be absent when GlobalObjectId is null");

            return true;
        });
    }
}

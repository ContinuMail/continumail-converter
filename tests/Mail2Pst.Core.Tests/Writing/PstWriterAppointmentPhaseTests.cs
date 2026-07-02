// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterAppointmentPhaseTests
{
    [Fact]
    public void WritePlan_WritesAppointments_IntoIPFAppointmentFolder_AndCountsConverted()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var appointmentFolders = new List<IReadOnlyList<string>> { new[] { "Calendars", "Home" } };
            var appointments = new List<PlannedAppointment>
            {
                new()
                {
                    Appointment = new AppointmentRecord
                    {
                        Subject = "Team meeting",
                        SourceId = "appt-1",
                        StartUtc = new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc),
                        EndUtc   = new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc),
                    },
                    TargetFolderPath = new[] { "Calendars", "Home" },
                },
            };
            var report = new ConversionReport();

            new PstWriter().WritePlan(plan, new List<PlannedMessage>(),
                new List<PlannedContact>(), new List<IReadOnlyList<string>>(),
                new List<PlannedTask>(), new List<IReadOnlyList<string>>(),
                appointments, appointmentFolders,
                dir, report);

            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                PSTFolder calRoot = f.TopOfPersonalFolders.FindChildFolder("Calendars");
                Assert.NotNull(calRoot);
                PSTFolder homeFolder = calRoot.FindChildFolder("Home");
                Assert.NotNull(homeFolder);
                Assert.Equal("IPF.Appointment", homeFolder.ContainerClass);
                var calFolder = Assert.IsType<CalendarFolder>(homeFolder);
                Assert.Equal(1, calFolder.AppointmentCount);
                Appointment appt = calFolder.GetAppointment(0);
                Assert.Equal("IPM.Appointment", appt.MessageClass);
                Assert.Equal(1, report.AppointmentsConverted);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Pre-merge review #8: a recoverable write-time attachment failure (here: a local-file ByValue
    /// attachment whose file does not exist → IOException when read at write time) must be recorded
    /// as a per-item skip, NOT abort the whole phase. A good appointment written afterwards must still
    /// land, and the store must reopen cleanly. Before the fix only ConfigValidationException was
    /// caught, so the IOException was fatal and deleted the in-progress part (all prior items).
    /// </summary>
    [Fact]
    public void WritePlan_AppointmentWithUnreadableAttachment_IsSkipped_NotFatal()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string missing = Path.Combine(dir, "does-not-exist.bin");
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var appointmentFolders = new List<IReadOnlyList<string>> { new[] { "Calendars", "Home" } };
            var appointments = new List<PlannedAppointment>
            {
                new()
                {
                    Appointment = new AppointmentRecord
                    {
                        Subject = "Bad attachment", SourceId = "appt-bad",
                        StartUtc = new DateTime(2026, 6, 30, 9, 0, 0, DateTimeKind.Utc),
                        EndUtc   = new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc),
                        Attachments = new[]
                        {
                            new CalendarAttachment(CalendarAttachmentKind.LocalFileByValue,
                                "does-not-exist.bin", "application/octet-stream", null, missing, null),
                        },
                    },
                    TargetFolderPath = new[] { "Calendars", "Home" },
                },
                new()
                {
                    Appointment = new AppointmentRecord
                    {
                        Subject = "Good appointment", SourceId = "appt-good",
                        StartUtc = new DateTime(2026, 6, 30, 11, 0, 0, DateTimeKind.Utc),
                        EndUtc   = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc),
                    },
                    TargetFolderPath = new[] { "Calendars", "Home" },
                },
            };
            var report = new ConversionReport();

            // Must not throw.
            new PstWriter().WritePlan(plan, new List<PlannedMessage>(),
                new List<PlannedContact>(), new List<IReadOnlyList<string>>(),
                new List<PlannedTask>(), new List<IReadOnlyList<string>>(),
                appointments, appointmentFolders, dir, report);

            Assert.Equal(1, report.AppointmentsConverted);       // only the good one
            Assert.True(report.AppointmentsSkipped >= 1);        // the bad one skipped

            // Store must reopen cleanly with exactly the good appointment.
            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                var cal = Assert.IsType<CalendarFolder>(
                    f.TopOfPersonalFolders.FindChildFolder("Calendars").FindChildFolder("Home"));
                Assert.Equal(1, cal.AppointmentCount);
                Assert.Equal("Good appointment", cal.GetAppointment(0).Subject);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Pre-merge review #7: the appointment/task size estimate (used for maxSizeMB split sizing) must
    /// include attachment bytes — otherwise a folder of large-attachment events silently blows past the
    /// split cap. A 10 KB inline attachment must add ≥ 10 KB to the estimate.
    /// </summary>
    [Fact]
    public void EstimateAppointmentSize_IncludesAttachmentBytes()
    {
        var without = new AppointmentRecord { Subject = "s" };
        var with = new AppointmentRecord
        {
            Subject = "s",
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes, "f.bin",
                    "application/octet-stream", new byte[10_000], null, null),
            },
        };
        long delta = PstWriter.EstimateAppointmentSize(with) - PstWriter.EstimateAppointmentSize(without);
        Assert.True(delta >= 10_000, $"attachment bytes must be counted in the estimate; delta={delta}");
    }

    /// <summary>Pre-merge review #7: the task estimate must also include attachment bytes.</summary>
    [Fact]
    public void EstimateTaskSize_IncludesAttachmentBytes()
    {
        var without = new TaskRecord { Subject = "s" };
        var with = new TaskRecord
        {
            Subject = "s",
            Attachments = new[]
            {
                new CalendarAttachment(CalendarAttachmentKind.InlineBytes, "f.bin",
                    "application/octet-stream", new byte[10_000], null, null),
            },
        };
        long delta = PstWriter.EstimateTaskSize(with) - PstWriter.EstimateTaskSize(without);
        Assert.True(delta >= 10_000, $"attachment bytes must be counted in the estimate; delta={delta}");
    }

    [Fact]
    public void WritePlan_EmptyAppointmentFolders_StillCreatesIPFAppointmentFolder()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var appointmentFolders = new List<IReadOnlyList<string>> { new[] { "Calendars", "Work" } };
            var report = new ConversionReport();

            new PstWriter().WritePlan(plan, new List<PlannedMessage>(),
                new List<PlannedContact>(), new List<IReadOnlyList<string>>(),
                new List<PlannedTask>(), new List<IReadOnlyList<string>>(),
                new List<PlannedAppointment>(), appointmentFolders,
                dir, report);

            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                PSTFolder calRoot = f.TopOfPersonalFolders.FindChildFolder("Calendars");
                Assert.NotNull(calRoot);
                PSTFolder workFolder = calRoot.FindChildFolder("Work");
                Assert.NotNull(workFolder);
                Assert.Equal("IPF.Appointment", workFolder.ContainerClass);
                Assert.IsType<CalendarFolder>(workFolder);
                Assert.Equal(0, report.AppointmentsConverted);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }
}

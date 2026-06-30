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
                Assert.Equal(0, report.AppointmentsConverted);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }
}

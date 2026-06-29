// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Reporting;

public class ConversionReportCalendarCountersTests
{
    [Fact]
    public void Appointment_and_task_counters_increment_independently()
    {
        var r = new ConversionReport();
        r.RecordAppointmentConverted();
        r.RecordAppointmentConverted();
        r.RecordAppointmentSkipped("Home", "bad recurrence");
        r.RecordAppointmentWarning("tz fallback");
        r.RecordTaskConverted();
        r.RecordTaskSkipped("Home", "bad due date");

        Assert.Equal(2, r.AppointmentsConverted);
        Assert.Equal(1, r.AppointmentsSkipped);
        Assert.Equal(1, r.AppointmentWarningCount);
        Assert.Equal(1, r.TasksConverted);
        Assert.Equal(1, r.TasksSkipped);
        // exactly 3 shared warnings: appointment-skipped, appointment-warning, task-skipped
        Assert.Equal(3, r.WarningCount);
    }

    [Fact]
    public void Counters_default_to_zero()
    {
        var r = new ConversionReport();
        Assert.Equal(0, r.AppointmentsConverted);
        Assert.Equal(0, r.TasksConverted);
        Assert.Equal(0, r.AppointmentsSkipped);
        Assert.Equal(0, r.TasksSkipped);
    }
}

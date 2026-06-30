// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// TDD read-back tests for <see cref="AppointmentWriter"/> driving the vendored
/// <see cref="SingleAppointment"/> substrate. Each test writes one appointment, closes,
/// reopens, and asserts specific MAPI properties round-trip correctly.
///
/// Lifecycle order (mirrors AppointmentWriteSmokeTests):
///   BeginSavingChanges → WriteAppointment → folder.SaveChanges → EndSavingChanges.
/// </summary>
public class AppointmentWriterTests
{
    // -----------------------------------------------------------------------
    // Round-trip infrastructure
    // -----------------------------------------------------------------------

    /// <summary>
    /// Write one appointment, close, reopen, expose the PST and Appointment to
    /// <paramref name="read"/>, close, delete. Handles teardown even if assertions throw.
    /// </summary>
    private static T RoundTripAppointment<T>(AppointmentRecord record, Func<PSTFile, Appointment, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-aw-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            // Write phase
            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();   // required before named-property allocation
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder(
                    "Calendar", FolderItemTypeName.Appointment);
                new AppointmentWriter().WriteAppointment(pst, folder, record);
                folder.SaveChanges();       // BEFORE EndSavingChanges (vendored gotcha)
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            // Read phase
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

    /// <summary>Resolve a named-prop ID on a reopen (the prop must have been written already).</summary>
    private static PropertyID Named(PSTFile pst, PropertyLongID lid, Guid set)
        => pst.NameToIDMap.ObtainIDFromName(new PropertyName(lid, set));

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void WriteAppointment_message_class_is_IPM_Appointment()
    {
        // Guards against accidentally writing a Note/Task into an Appointment folder.
        var a = new AppointmentRecord
        {
            Subject  = "Class check",
            StartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
        };

        string? cls = RoundTripAppointment(a,
            (_, appt) => appt.PC.GetStringProperty(PropertyID.PidTagMessageClass));
        Assert.Equal("IPM.Appointment", cls);
    }

    [Fact]
    public void WriteAppointment_timed_with_timezone_round_trips_start_end_and_tz_definition()
    {
        var start = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc);

        // Romance Standard Time (CET/CEST) — used in the Task 0 ground-truth dump.
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

        var a = new AppointmentRecord
        {
            Subject  = "Timed with TZ",
            StartUtc = start,
            EndUtc   = end,
            TimeZone = tz,
        };

        RoundTripAppointment(a, (_, appt) =>
        {
            Assert.Equal("Timed with TZ", appt.Subject);

            // PidLidAppointmentStartWhole must round-trip as the original UTC instant.
            Assert.Equal(start, appt.StartDTUtc);

            // Duration derived from EndWhole − StartWhole (SetStartAndDuration guarantees this).
            Assert.Equal(30, appt.Duration);

            // The timezone definition blob must be present for a timed appointment with a known zone.
            Assert.True(appt.HasTimeZoneDefinition,
                "Expected PidLidAppointmentTimeZoneDefinitionStartDisplay to be written for a timed appointment with a timezone");

            return true;
        });
    }

    [Fact]
    public void WriteAppointment_allday_matches_dump()
    {
        // Reproduces the Task 0 all-day dump invariants:
        //   AppointmentSubType (IsAllDayEvent) == true, Duration == 1440 min, StartWhole = mapper-supplied midnight UTC.
        // The mapper (Task 3) computes the local-midnight-in-UTC values; here we use plain UTC midnight
        // to keep the test self-contained.
        var utcMidnight = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var utcNextDay  = utcMidnight.AddMinutes(1440);

        var a = new AppointmentRecord
        {
            Subject    = "All-day event",
            StartUtc   = utcMidnight,
            EndUtc     = utcNextDay,
            IsAllDay   = true,
            BusyStatus = 0,   // Free — default for all-day events per Task 0 dump
        };

        RoundTripAppointment(a, (_, appt) =>
        {
            Assert.True(appt.IsAllDayEvent,
                "PidLidAppointmentSubType (IsAllDayEvent) must be true");
            Assert.Equal(1440, appt.Duration);
            Assert.Equal(utcMidnight, appt.StartDTUtc);
            return true;
        });
    }

    [Fact]
    public void WriteAppointment_free_and_busy_status_round_trips()
    {
        // Free=0
        var aFree = new AppointmentRecord
        {
            Subject    = "Free slot",
            StartUtc   = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc     = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            BusyStatus = 0,
        };
        BusyStatus freeResult = RoundTripAppointment(aFree, (_, appt) => appt.BusyStatus);
        Assert.Equal(BusyStatus.Avaiable, freeResult);

        // Busy=2
        var aBusy = new AppointmentRecord
        {
            Subject    = "Busy slot",
            StartUtc   = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc),
            EndUtc     = new DateTime(2026, 7, 2, 11, 0, 0, DateTimeKind.Utc),
            BusyStatus = 2,
        };
        BusyStatus busyResult = RoundTripAppointment(aBusy, (_, appt) => appt.BusyStatus);
        Assert.Equal(BusyStatus.Busy, busyResult);
    }

    [Fact]
    public void WriteAppointment_private_sets_sensitivity_and_private_flag()
    {
        // A Private appointment must set BOTH PidTagSensitivity=2 AND PidLidPrivate=true
        // (mirroring the Task 0 ground-truth and TaskWriter Private coupling).
        var a = new AppointmentRecord
        {
            Subject     = "Secret meeting",
            StartUtc    = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc      = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            Sensitivity = 2,   // Private
        };

        RoundTripAppointment(a, (_, appt) =>
        {
            Assert.Equal(2, appt.PC.GetInt32Property(PropertyID.PidTagSensitivity));
            Assert.True(appt.IsPrivate, "PidLidPrivate must be true for Sensitivity=2");
            return true;
        });
    }

    [Fact]
    public void WriteAppointment_reminder_with_15min_delta_and_signal_time()
    {
        var start = new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc);
        var a = new AppointmentRecord
        {
            Subject               = "Dentist",
            StartUtc              = start,
            EndUtc                = start.AddMinutes(60),
            ReminderSet           = true,
            ReminderMinutesBefore = 15,
        };

        RoundTripAppointment(a, (pst, appt) =>
        {
            // PidLidReminderSet (PSETID_Common 0x8503) == true
            PropertyID rsId = Named(pst, PropertyLongID.PidLidReminderSet, PropertySetGuid.PSETID_Common);
            Assert.True(appt.PC.GetBooleanProperty(rsId));

            // PidLidReminderDelta (PSETID_Common 0x8501) == 15 minutes
            PropertyID rdId = Named(pst, PropertyLongID.PidLidReminderDelta, PropertySetGuid.PSETID_Common);
            Assert.Equal(15, appt.PC.GetInt32Property(rdId));

            // PidLidReminderSignalTime (PSETID_Common 0x8560) == start − 15 min
            PropertyID rstId = Named(pst, PropertyLongID.PidLidReminderSignalTime, PropertySetGuid.PSETID_Common);
            DateTime? signal = appt.PC.GetDateTimeProperty(rstId);
            Assert.NotNull(signal);
            Assert.Equal(start.AddMinutes(-15), signal!.Value);

            return true;
        });
    }

    [Fact]
    public void WriteAppointment_categories_round_trip()
    {
        var a = new AppointmentRecord
        {
            Subject    = "Tagged event",
            StartUtc   = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc     = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            Categories = new[] { "Work", "Meeting" },
        };

        IReadOnlyList<string>? cats = RoundTripAppointment(a, (pst, appt) =>
        {
            // Resolve the dynamic PropertyID for "Keywords" (string-named prop, guidHint=2=PS_PUBLIC_STRINGS)
            ushort? kid = PropertyNameToIDMap.ResolveStringNamedProperty(pst, 2, "Keywords");
            Assert.NotNull(kid);
            var rec = appt.PC.GetRecordByPropertyID((PropertyID)kid!.Value);
            Assert.NotNull(rec);
            return (IReadOnlyList<string>)PropertyContext.DeserializeMultiString(
                appt.PC.GetExternalRecordData(rec!));
        });

        Assert.Equal(new[] { "Work", "Meeting" }, cats);
    }

    [Fact]
    public void WriteAppointment_importance_high_round_trips()
    {
        var a = new AppointmentRecord
        {
            Subject    = "Critical meeting",
            StartUtc   = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc     = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            Importance = 2,   // High
        };

        int? imp = RoundTripAppointment(a, (_, appt) =>
            appt.PC.GetInt32Property(PropertyID.PidTagImportance));
        Assert.Equal(2, imp);
    }

    [Fact]
    public void WriteAppointment_html_only_body_writes_html_native_and_derived_plain()
    {
        // HTML-only body (Body=null): writer must produce PidTagHtml + PidTagNativeBody=3
        // AND a derived PidTagBody via PstWriter.HtmlToPlainText (no duplicated logic).
        const string html = "<html><body><b>Hello</b> world</body></html>";
        var a = new AppointmentRecord
        {
            Subject  = "HTML body",
            StartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            BodyHtml = html,
            Body     = null,   // no explicit plain text — must be auto-derived
        };

        RoundTripAppointment(a, (_, appt) =>
        {
            // PidTagHtml must contain the original HTML as UTF-8 bytes
            byte[]? htmlBytes = appt.PC.GetBytesProperty(PropertyID.PidTagHtml);
            Assert.NotNull(htmlBytes);
            Assert.Equal(html, Encoding.UTF8.GetString(htmlBytes!));

            // PidTagNativeBody must be 3 (HTML)
            Assert.Equal(3, appt.PC.GetInt32Property(PropertyID.PidTagNativeBody));

            // PidTagBody must be present and non-empty (derived from HTML via HtmlToPlainText)
            string? plain = appt.PC.GetStringProperty(PropertyID.PidTagBody);
            Assert.NotNull(plain);
            Assert.False(string.IsNullOrWhiteSpace(plain), "Derived plain body must not be blank");
            // Basic smoke: the tag-stripped result must contain some content from the HTML
            Assert.Contains("Hello", plain, StringComparison.OrdinalIgnoreCase);

            return true;
        });
    }

    [Fact]
    public void WriteAppointment_floating_tz_does_not_touch_registry_and_writes_no_tz_blob()
    {
        // TimeZone=null means a floating/unresolved timezone. The write path must NOT call
        // SetOriginalTimeZone — which would be the only path to Win32 registry access.
        // On non-Windows platforms this path would throw PlatformNotSupportedException.
        // Assert: write completes without exception AND no tz-definition blob is written.
        var a = new AppointmentRecord
        {
            Subject  = "Floating event",
            StartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            TimeZone = null,   // floating — writer must skip SetOriginalTimeZone
        };

        bool completed = RoundTripAppointment(a, (_, appt) =>
        {
            // No PidLidAppointmentTimeZoneDefinitionStartDisplay blob for a floating appointment.
            Assert.False(appt.HasTimeZoneDefinition,
                "Floating appointment must NOT have PidLidAppointmentTimeZoneDefinitionStartDisplay");
            return true;
        });

        // If we reach here, no exception was thrown during write — registry access was skipped.
        Assert.True(completed);
    }

    [Fact]
    public void WriteAppointment_out_of_range_busy_status_normalizes_to_busy()
    {
        // Defensive normalization: BusyStatus=9 is invalid; writer must clamp to Busy=2.
        var a = new AppointmentRecord
        {
            Subject    = "Bad busy value",
            StartUtc   = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc     = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            BusyStatus = 9,   // out of range — must normalize to Busy=2
        };

        BusyStatus result = RoundTripAppointment(a, (_, appt) => appt.BusyStatus);
        Assert.Equal(BusyStatus.Busy, result);
    }
}

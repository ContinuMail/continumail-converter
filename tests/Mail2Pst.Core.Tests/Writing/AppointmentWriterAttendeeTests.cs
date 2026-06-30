// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

/// <summary>
/// TDD tests for the attendee/organizer/meeting-state block in <see cref="AppointmentWriter"/>.
/// Each test writes one appointment (with or without attendees), closes, reopens, and asserts
/// the MAPI recipient table, SentRepresenting*, StateFlags, and PidLidResponseStatus round-trip.
///
/// Lifecycle matches AppointmentWriterTests: BeginSavingChanges → WriteAppointment →
/// folder.SaveChanges → EndSavingChanges.
/// </summary>
public class AppointmentWriterAttendeeTests
{
    // -----------------------------------------------------------------------
    // Round-trip infrastructure (mirrors AppointmentWriterTests)
    // -----------------------------------------------------------------------

    private static T RoundTripAppointment<T>(AppointmentRecord record, Func<PSTFile, Appointment, T> read)
    {
        string path = Path.Combine(Path.GetTempPath(), $"m2p-att-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            // Write phase
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

    /// <summary>Resolve a named-prop ID on a reopen (allocates if absent — use for props known to have been written).</summary>
    private static PropertyID Named(PSTFile pst, PropertyLongID lid, Guid set)
        => pst.NameToIDMap.ObtainIDFromName(new PropertyName(lid, set));

    /// <summary>
    /// Look up a named-prop ID without allocating. Returns null when the prop was never
    /// registered in this PST — the correct "absent" check on a read-only file.
    /// </summary>
    private static PropertyID? TryNamed(PSTFile pst, PropertyLongID lid, Guid set)
        => pst.NameToIDMap.GetIDFromName(new PropertyName(lid, set));

    /// <summary>Read RecipientType for row <paramref name="rowIndex"/> from the table.</summary>
    private static int? RowRecipientType(Appointment appt, int rowIndex)
        => appt.RecipientsTable?.GetInt32Property(rowIndex, PropertyID.PidTagRecipientType);

    // -----------------------------------------------------------------------
    // Helpers: canned records
    // -----------------------------------------------------------------------

    private static AppointmentRecord MakePlainRecord(string subject = "Plain event") => new AppointmentRecord
    {
        Subject  = subject,
        StartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
        EndUtc   = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
    };

    private static AppointmentRecord MakeMeetingRecord(
        bool organizerHasEmail = true,
        AttendeeResponse organizerResponse = AttendeeResponse.None) => new AppointmentRecord
    {
        Subject  = "Team meeting",
        StartUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
        EndUtc   = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
        Organizer = new AppointmentAttendee
        {
            DisplayName = "Alice Organizer",
            Email       = organizerHasEmail ? "alice@example.com" : "",
            Kind        = AttendeeKind.Required,
            Response    = organizerResponse,
            IsOrganizer = true,
        },
        Attendees = new[]
        {
            new AppointmentAttendee { DisplayName = "Bob Required1", Email = "bob1@example.com", Kind = AttendeeKind.Required, Response = AttendeeResponse.Accepted },
            new AppointmentAttendee { DisplayName = "Carol Required2", Email = "carol@example.com", Kind = AttendeeKind.Required, Response = AttendeeResponse.None },
            new AppointmentAttendee { DisplayName = "Dave Optional", Email = "dave@example.com", Kind = AttendeeKind.Optional, Response = AttendeeResponse.Declined },
            new AppointmentAttendee { DisplayName = "Room Resource", Email = "room@example.com", Kind = AttendeeKind.Resource, Response = AttendeeResponse.None },
        },
    };

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Meeting_RecipientCount_is_5_and_types_are_correct()
    {
        // organizer (To, isOrganizer) + 2×Required (To) + 1×Optional (Cc) + 1×Resource (Bcc) = 5
        var a = MakeMeetingRecord();

        RoundTripAppointment(a, (_, appt) =>
        {
            Assert.Equal(5, appt.RecipientCount);

            // Row 0 = organizer (isOrganizer=true, To)
            MessageRecipient org = appt.GetRecipient(0);
            Assert.True(org.IsOrganizer, "Row 0 must be IsOrganizer");
            Assert.Equal((int)RecipientType.To, RowRecipientType(appt, 0));

            // Rows 1-2 = Required → To
            Assert.Equal((int)RecipientType.To, RowRecipientType(appt, 1));
            Assert.Equal((int)RecipientType.To, RowRecipientType(appt, 2));

            // Row 3 = Optional → Cc
            Assert.Equal((int)RecipientType.Cc, RowRecipientType(appt, 3));

            // Row 4 = Resource → Bcc
            Assert.Equal((int)RecipientType.Bcc, RowRecipientType(appt, 4));

            // Message class must stay IPM.Appointment even for a meeting
            Assert.Equal("IPM.Appointment", appt.PC.GetStringProperty(PropertyID.PidTagMessageClass));

            return true;
        });
    }

    [Fact]
    public void Accepted_attendee_ResponseStatus_is_3_on_readback()
    {
        // Bob Required1 has Response = Accepted (3).
        var a = MakeMeetingRecord();

        RoundTripAppointment(a, (_, appt) =>
        {
            // Row 1 = Bob Required1 (Accepted)
            MessageRecipient bob = appt.GetRecipient(1);
            Assert.Equal((int)AttendeeResponse.Accepted, bob.ResponseStatus);
            return true;
        });
    }

    [Fact]
    public void Organizer_SentRepresenting_is_set_and_row_ResponseStatus_is_Organized_regardless_of_record()
    {
        // Organizer.Response = None (0) but the writer must FORCE the organizer row to Organized(1).
        var a = MakeMeetingRecord(organizerHasEmail: true, organizerResponse: AttendeeResponse.None);

        RoundTripAppointment(a, (_, appt) =>
        {
            // SentRepresentingName must be set to the organizer's display name.
            string? name = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingName);
            Assert.Equal("Alice Organizer", name);

            // SentRepresentingEmailAddress must be set (organizer has an email).
            string? email = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingEmailAddress);
            Assert.Equal("alice@example.com", email);

            // Organizer recipient row (row 0) ResponseStatus must be Organized(1) — writer forces it.
            MessageRecipient orgRow = appt.GetRecipient(0);
            Assert.Equal((int)AttendeeResponse.Organized, orgRow.ResponseStatus);

            return true;
        });
    }

    [Fact]
    public void Meeting_StateFlags_has_asfMeeting_bit_and_PidLidResponseStatus_is_Organized()
    {
        var a = MakeMeetingRecord();

        RoundTripAppointment(a, (pst, appt) =>
        {
            // PidLidAppointmentStateFlags (PSETID_Appointment 0x8217): asfMeeting bit (0x1) must be set.
            PropertyID sfId = Named(pst, PropertyLongID.PidLidAppointmentStateFlags, PropertySetGuid.PSETID_Appointment);
            int? stateFlags = appt.PC.GetInt32Property(sfId);
            Assert.NotNull(stateFlags);
            Assert.True((stateFlags!.Value & 1) != 0, "asfMeeting bit (0x1) must be set in PidLidAppointmentStateFlags");

            // PidLidResponseStatus (PSETID_Appointment 0x8218) must be Organized(1).
            PropertyID rsId = Named(pst, PropertyLongID.PidLidResponseStatus, PropertySetGuid.PSETID_Appointment);
            int? rs = appt.PC.GetInt32Property(rsId);
            Assert.Equal((int)AttendeeResponse.Organized, rs);

            return true;
        });
    }

    [Fact]
    public void No_attendee_appointment_writes_no_recipients_no_SentRepresenting_no_meeting_props()
    {
        // Regression guard: a plain appointment (no attendees) must NOT write any meeting state.
        var a = MakePlainRecord("Plain appointment");

        RoundTripAppointment(a, (pst, appt) =>
        {
            // No recipient table rows.
            Assert.Equal(0, appt.RecipientCount);

            // No SentRepresentingName.
            string? name = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingName);
            Assert.True(string.IsNullOrEmpty(name), "SentRepresentingName must not be set for a plain appointment");

            // PidLidAppointmentStateFlags must not be present (plain appointment).
            PropertyID sfId = Named(pst, PropertyLongID.PidLidAppointmentStateFlags, PropertySetGuid.PSETID_Appointment);
            int? stateFlags = appt.PC.GetInt32Property(sfId);
            Assert.Null(stateFlags);

            return true;
        });
    }

    [Fact]
    public void OrganizerOnly_record_is_treated_as_plain_appointment()
    {
        // Organizer set but Attendees is empty → WriteAttendees returns early → no recipient rows,
        // no SentRepresenting*, no meeting-state props.
        var a = new AppointmentRecord
        {
            Subject  = "Organizer-only event",
            StartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            Organizer = new AppointmentAttendee
            {
                DisplayName = "Alice Organizer",
                Email       = "alice@example.com",
                Kind        = AttendeeKind.Required,
                IsOrganizer = true,
            },
            Attendees = Array.Empty<AppointmentAttendee>(),  // empty — must NOT promote to meeting
        };

        RoundTripAppointment(a, (pst, appt) =>
        {
            Assert.Equal(0, appt.RecipientCount);

            string? name = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingName);
            Assert.True(string.IsNullOrEmpty(name), "SentRepresentingName must not be set for an organizer-only appointment");

            PropertyID sfId = Named(pst, PropertyLongID.PidLidAppointmentStateFlags, PropertySetGuid.PSETID_Appointment);
            int? stateFlags = appt.PC.GetInt32Property(sfId);
            Assert.Null(stateFlags);

            return true;
        });
    }

    [Fact]
    public void Organizer_with_empty_email_omits_organizer_row_but_still_writes_attendees()
    {
        // Organizer without an email address: SentRepresentingName is set (display-only), but no
        // organizer recipient row is written (a row with empty address is invalid MAPI).
        // Attendee rows must still be written.
        var a = MakeMeetingRecord(organizerHasEmail: false);

        RoundTripAppointment(a, (_, appt) =>
        {
            // 4 attendees but NO organizer row → total 4 rows
            Assert.Equal(4, appt.RecipientCount);

            // No row should be IsOrganizer=true
            for (int i = 0; i < appt.RecipientCount; i++)
                Assert.False(appt.GetRecipient(i).IsOrganizer, $"Row {i} must not have IsOrganizer=true");

            // SentRepresentingName is still set (display-only organizer)
            string? name = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingName);
            Assert.Equal("Alice Organizer", name);

            // SentRepresentingEmailAddress must NOT be set (empty email)
            string? email = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingEmailAddress);
            Assert.True(string.IsNullOrEmpty(email), "SentRepresentingEmailAddress must not be set when organizer email is empty");

            return true;
        });
    }

    [Fact]
    public void Meeting_message_class_stays_IPM_Appointment()
    {
        // The recipient/meeting-state block must not change the message class.
        var a = MakeMeetingRecord();
        string? cls = RoundTripAppointment(a,
            (_, appt) => appt.PC.GetStringProperty(PropertyID.PidTagMessageClass));
        Assert.Equal("IPM.Appointment", cls);
    }

    // -----------------------------------------------------------------------
    // Item 1 (TDD): Organizer == null → StateFlags set, PidLidResponseStatus absent
    // -----------------------------------------------------------------------

    [Fact]
    public void AttendeesWithoutOrganizer_StateFlagsSet_but_PidLidResponseStatus_absent()
    {
        // A meeting with attendees but NO explicit Organizer.
        // The asfMeeting bit in PidLidAppointmentStateFlags MUST still be set (it's meeting-presence
        // derived from Attendees.Count > 0), but PidLidResponseStatus must NOT be written —
        // the organizer-copy response-status is meaningless when no organizer is known.
        var a = new AppointmentRecord
        {
            Subject  = "Attendees but no organizer",
            StartUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            Organizer = null,
            Attendees = new[]
            {
                new AppointmentAttendee { DisplayName = "Bob", Email = "bob@example.com",
                    Kind = AttendeeKind.Required, Response = AttendeeResponse.Accepted },
            },
        };

        RoundTripAppointment(a, (pst, appt) =>
        {
            // asfMeeting bit must be set (Attendees.Count > 0 promotes to meeting).
            PropertyID sfId = Named(pst, PropertyLongID.PidLidAppointmentStateFlags, PropertySetGuid.PSETID_Appointment);
            int? stateFlags = appt.PC.GetInt32Property(sfId);
            Assert.NotNull(stateFlags);
            Assert.True((stateFlags!.Value & 1) != 0, "asfMeeting bit (0x1) must be set even without an explicit organizer");

            // PidLidResponseStatus must NOT be present when Organizer is null.
            // Use TryNamed (GetIDFromName, nullable) — ObtainIDFromName would try to
            // allocate a new entry on the read-only file and throw if the prop was never registered.
            PropertyID? rsId = TryNamed(pst, PropertyLongID.PidLidResponseStatus, PropertySetGuid.PSETID_Appointment);
            int? rs = rsId.HasValue ? appt.PC.GetInt32Property(rsId.Value) : null;
            Assert.Null(rs);

            return true;
        });
    }

    // -----------------------------------------------------------------------
    // Item 5 (T3): SentRepresentingAddressType assertions
    // -----------------------------------------------------------------------

    [Fact]
    public void Organizer_SentRepresentingAddressType_is_SMTP_when_email_present()
    {
        // When organizer has an email, PidTagSentRepresentingAddressType must be "SMTP".
        var a = MakeMeetingRecord(organizerHasEmail: true);

        RoundTripAppointment(a, (_, appt) =>
        {
            string? addrType = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingAddressType);
            Assert.Equal("SMTP", addrType);
            return true;
        });
    }

    [Fact]
    public void Organizer_SentRepresentingAddressType_absent_when_email_empty()
    {
        // When organizer has no email, PidTagSentRepresentingAddressType must NOT be written.
        var a = MakeMeetingRecord(organizerHasEmail: false);

        RoundTripAppointment(a, (_, appt) =>
        {
            string? addrType = appt.PC.GetStringProperty(PropertyID.PidTagSentRepresentingAddressType);
            Assert.True(string.IsNullOrEmpty(addrType),
                "PidTagSentRepresentingAddressType must not be set when organizer email is empty");
            return true;
        });
    }
}

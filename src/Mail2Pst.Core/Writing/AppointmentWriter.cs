// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Mail2Pst.Core.Models;
using PSTFileFormat;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Writes an <see cref="AppointmentRecord"/> as an IPM.Appointment item into an
/// IPF.Appointment (CalendarFolder) via the vendored <see cref="SingleAppointment"/> substrate.
/// </summary>
/// <remarks>
/// MAPI recipe ground-truthed from a real Outlook appointment export (Task 0, 2026-06-30).
/// Key decisions:
/// <list type="bullet">
///   <item>Timezone via <c>SetOriginalTimeZone(tz)</c> — only when <see cref="AppointmentRecord.TimeZone"/>
///         is non-null; floating events (null tz) skip it entirely and make no Win32 registry calls.</item>
///   <item>Reminder uses a MINUTES-BEFORE DELTA (<c>PidLidReminderDelta</c>) + derived signal time —
///         unlike tasks, which use an absolute reminder instant.</item>
///   <item><c>PidLidMeetingStatus</c> is intentionally NOT set: absent in the Task 0 dump for plain appointments.</item>
/// </list>
/// The caller must call <see cref="PSTFile.BeginSavingChanges"/> before this method
/// (named-property allocation requires it) and <see cref="PSTFolder.SaveChanges"/> +
/// <see cref="PSTFile.EndSavingChanges"/> after.
/// </remarks>
public sealed class AppointmentWriter
{
    /// <summary>
    /// Writes one appointment into <paramref name="folder"/> inside <paramref name="file"/>.
    /// </summary>
    public void WriteAppointment(PSTFile file, PSTFolder folder, AppointmentRecord a)
    {
        SingleAppointment appt = SingleAppointment.CreateNewSingleAppointment(file, folder.NodeID);
        appt.InternetCodepage = 65001;  // override the 1255 Hebrew default (CreateNewSingleAppointment gotcha)

        appt.Subject = a.Subject;

        // Defensive normalization — AppointmentWriter must never emit invalid MAPI even if handed a
        // bad AppointmentRecord (it is called directly in tests / future pipelines, not only via the mapper).
        int busy        = a.BusyStatus  is >= 0 and <= 3  ? a.BusyStatus  : 2;   // default Busy
        int importance  = a.Importance  is >= 0 and <= 2  ? a.Importance  : 1;
        int sensitivity = a.Sensitivity is 0 or 2 or 3   ? a.Sensitivity : 0;

        // SetStartAndDuration writes PidLidAppointmentStartWhole / EndWhole + Clip + Common.
        int durationMinutes = (int)Math.Max(0, Math.Round((a.EndUtc - a.StartUtc).TotalMinutes));
        appt.SetStartAndDuration(a.StartUtc, durationMinutes);

        // PidLidAppointmentSubType — set BEFORE timezone blob so Outlook reads all-day correctly.
        appt.IsAllDayEvent = a.IsAllDay;

        // Timezone blob (PidLidAppointmentTimeZoneDefinitionStartDisplay):
        //   - Timed events AND all-day events write the blob when a resolved zone is available.
        //   - Floating/unresolved events (TimeZone == null) skip it entirely — NO Win32 registry
        //     access is made on this path (critical for cross-platform correctness).
        if (a.TimeZone is { } tz)
            appt.SetOriginalTimeZone(tz);   // Appointment.SetOriginalTimeZone(TimeZoneInfo) single-arg overload

        if (!string.IsNullOrEmpty(a.Location)) appt.Location = a.Location;
        appt.BusyStatus = (BusyStatus)busy;

        // PidLidPrivate (PSETID_Common 0x8506): coupled to Sensitivity==2 only.
        // Confidential (3) intentionally does NOT set it — matches Outlook ground truth (Task 0 dump).
        appt.IsPrivate = sensitivity == 2;

        // Static-tag props (same pattern as TaskWriter)
        appt.PC.SetInt32Property(PropertyID.PidTagImportance, importance);
        appt.PC.SetInt32Property(PropertyID.PidTagSensitivity, sensitivity);

        WriteBody(appt, a);

        // Categories — reuse the mail / task "Keywords" MV-string path
        if (a.Categories.Count > 0)
        {
            ushort kw = PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords");
            appt.PC.SetMultiStringProperty((PropertyID)kw, a.Categories);
        }

        // Reminder — appointments use a MINUTES-BEFORE DELTA (unlike tasks which use absolute time).
        // PidLidReminderSet is written via the vendor IsReminderSet setter (PSETID_Common 0x8503).
        // PidLidReminderDelta (0x8501) and PidLidReminderSignalTime (0x8560) via the named-prop path.
        appt.IsReminderSet = a.ReminderSet;
        if (a.ReminderSet)
        {
            PropertyID deltaId = file.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidReminderDelta, PropertySetGuid.PSETID_Common));
            appt.PC.SetInt32Property(deltaId, a.ReminderMinutesBefore);

            PropertyID signalId = file.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidReminderSignalTime, PropertySetGuid.PSETID_Common));
            // Signal time = start − delta (UTC). This is the instant Outlook fires the reminder.
            DateTime signalTime = a.StartUtc.AddMinutes(-a.ReminderMinutesBefore);
            appt.PC.SetDateTimeProperty(signalId, signalTime);
        }

        // PidLidMeetingStatus is intentionally NOT set: absent in the Task 0 dump for plain appointments.

        WriteAttendees(file, appt, a);   // recipients + organizer + meeting state (no-op when no attendees)

        appt.SaveChanges();
        folder.AddMessage(appt);
        // folder.SaveChanges() is the caller's responsibility (must be called before EndSavingChanges).
    }

    private static RecipientType MapRecipientType(AttendeeKind kind) => kind switch
    {
        AttendeeKind.Optional => RecipientType.Cc,
        AttendeeKind.Resource => RecipientType.Bcc,
        _ => RecipientType.To,
    };

    /// <summary>
    /// Writes meeting attendees, organizer, and meeting-state props onto <paramref name="appt"/>.
    /// Promotes the item to a meeting ONLY when <paramref name="a"/> has ≥1 attendee —
    /// an organizer-only event is treated as a plain appointment (no recipient rows, no meeting state).
    /// </summary>
    private static void WriteAttendees(PSTFile file, SingleAppointment appt, AppointmentRecord a)
    {
        // PR6 promotes to a meeting ONLY when there is ≥1 non-organizer attendee. An organizer-only event
        // (just an ORGANIZER line, no attendees) is a plain appointment — no recipient rows, no meeting state
        // (avoids the hybrid "recipient table but not a meeting" state). The mapper guarantees every entry in
        // a.Attendees has a non-empty Email; the organizer may be display-only (no email).
        if (a.Attendees.Count == 0) return;

        // Organizer → sender (mail precedent): name always, SMTP address only when present.
        if (a.Organizer is { } org)
        {
            appt.SentRepresentingName = org.DisplayName;
            if (!string.IsNullOrEmpty(org.Email))
            {
                appt.SentRepresentingAddressType = "SMTP";
                appt.SentRepresentingEmailAddress = org.Email;
            }
        }

        // Recipient rows — Task 0 CONFIRMED the organizer is a MeetingOrganizer-flagged To row
        // (isOrganizer:true sets RecipientFlags.MeetingOrganizer) AND attendees are To/Cc/Bcc.
        // AddRecipients also builds PidTagDisplayTo/Cc/Bcc automatically.
        var recipients = new List<MessageRecipient>();

        // Organizer recipient row ONLY when it has an email (a row with an empty address is invalid MAPI).
        if (a.Organizer is { Email: { Length: > 0 } } orgWithEmail)
            recipients.Add(new MessageRecipient(orgWithEmail.DisplayName, orgWithEmail.Email, isOrganizer: true,
                RecipientType.To) { ResponseStatus = (int)AttendeeResponse.Organized });   // organizer copy = respOrganized; don't trust record.Response

        foreach (var att in a.Attendees)   // each has a non-empty Email (mapper-enforced)
            recipients.Add(new MessageRecipient(att.DisplayName, att.Email, isOrganizer: false,
                MapRecipientType(att.Kind)) { ResponseStatus = (int)att.Response });

        appt.AddRecipients(recipients);

        // asfMeeting = 0x1 (PidLidAppointmentStateFlags, PSETID_Appointment 0x8217)
        // The vendor Appointment.StateFlags setter takes int; there is no AppointmentStateFlags enum.
        appt.StateFlags = 1;   // asfMeeting (0x1)

        // PidLidResponseStatus = respOrganized(1) — PR6 organizer-copy default (Task 0 recipe).
        // Named prop registered in Task 2; PSETID_Appointment 0x8218.
        PropertyID rs = file.NameToIDMap.ObtainIDFromName(
            new PropertyName(PropertyLongID.PidLidResponseStatus, PropertySetGuid.PSETID_Appointment));
        appt.PC.SetInt32Property(rs, (int)AttendeeResponse.Organized);

        // Do NOT set PidLidFInvited (inconsistent in ground truth, not load-bearing) or
        // PidLidMeetingStatus/PidLidAppointmentReplyTime (absent in ground truth). GlobalObjectId → PR8.
    }

    // WriteBody rules (ground-truthed from PstWriter.WriteMessage pattern):
    //   - If a.Body present → PidTagBody (plain text).
    //   - If a.BodyHtml present → PidTagHtml (UTF-8 bytes) + PidTagNativeBody=3 + InternetCodepage=65001;
    //     AND ensure PidTagBody exists — derive it from HTML via PstWriter.HtmlToPlainText if a.Body is empty.
    //   - PstWriter.HtmlToPlainText is internal-static in the same assembly (Mail2Pst.Core); no HtmlBody.cs needed.
    private static void WriteBody(SingleAppointment appt, AppointmentRecord a)
    {
        string? plain = a.Body;
        if (!string.IsNullOrEmpty(a.BodyHtml))
        {
            appt.PC.SetBytesProperty(PropertyID.PidTagHtml, Encoding.UTF8.GetBytes(a.BodyHtml));
            appt.PC.SetInt32Property(PropertyID.PidTagNativeBody, 3);
            // InternetCodepage=65001 already set unconditionally at the top of WriteAppointment.
            if (string.IsNullOrEmpty(plain))
                plain = PstWriter.HtmlToPlainText(a.BodyHtml);
        }
        if (!string.IsNullOrEmpty(plain))
            appt.PC.SetStringProperty(PropertyID.PidTagBody, plain);
    }
}

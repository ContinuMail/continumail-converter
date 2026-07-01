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
/// IPF.Appointment (CalendarFolder) via the vendored <see cref="Appointment"/> substrate
/// (<see cref="SingleAppointment"/> for non-recurring, <see cref="RecurringAppointment"/>
/// for series masters).
/// </summary>
/// <remarks>
/// MAPI recipe ground-truthed from a real Outlook appointment export (Task 0, 2026-06-30).
/// Key decisions:
/// <list type="bullet">
///   <item>Timezone via <c>SetOriginalTimeZone(tz)</c> — only when a resolved zone is available;
///         floating events (null tz) skip it for single appointments. Recurring appointments
///         ALWAYS get a non-null zone (UTC fallback) so <c>RecurringAppointment.SaveChanges()</c>
///         never hits the Win32 system-TZ fallback.</item>
///   <item>Reminder uses a MINUTES-BEFORE DELTA (<c>PidLidReminderDelta</c>) + derived signal time —
///         unlike tasks, which use an absolute reminder instant.</item>
///   <item><c>PidLidMeetingStatus</c> is intentionally NOT set: absent in the Task 0 dump for plain appointments.</item>
///   <item>Common fields (body, categories, reminder, attendees) run for BOTH single and recurring paths.</item>
/// </list>
/// The caller must call <see cref="PSTFile.BeginSavingChanges"/> before this method
/// (named-property allocation requires it) and <see cref="PSTFolder.SaveChanges"/> +
/// <see cref="PSTFile.EndSavingChanges"/> after.
/// </remarks>
public sealed class AppointmentWriter
{
    /// <summary>
    /// Writes one appointment into <paramref name="folder"/> inside <paramref name="file"/>.
    /// Branches on <see cref="AppointmentRecord.Recurrence"/>: null → <see cref="SingleAppointment"/>;
    /// non-null → <see cref="RecurringAppointment"/>.
    /// </summary>
    public void WriteAppointment(PSTFile file, PSTFolder folder, AppointmentRecord a)
    {
        // Branch on recurrence: single vs. series master.
        Appointment appt = a.Recurrence is null
            ? SingleAppointment.CreateNewSingleAppointment(file, folder.NodeID)
            : RecurringAppointment.CreateNewRecurringAppointment(file, folder.NodeID);

        appt.InternetCodepage = 65001;  // override the 1255 Hebrew default (CreateNew* gotcha)
        appt.Subject = a.Subject;

        // Defensive normalization — AppointmentWriter must never emit invalid MAPI even if handed a
        // bad AppointmentRecord (it is called directly in tests / future pipelines, not only via the mapper).
        int busy        = a.BusyStatus  is >= 0 and <= 3  ? a.BusyStatus  : 2;   // default Busy
        int importance  = a.Importance  is >= 0 and <= 2  ? a.Importance  : 1;
        int sensitivity = a.Sensitivity is 0 or 2 or 3   ? a.Sensitivity : 0;

        // Resolve and apply timezone BEFORE SetStartAndDuration.
        // RecurringAppointment.StartDTUtc setter computes PidLidClipStart using this.OriginalTimeZone;
        // if the zone is applied AFTER, ClipStart is computed in the host's local zone (wrong on any
        // host whose local zone ≠ the event zone). SingleAppointment is unaffected (its ClipStart
        // setter does not use OriginalTimeZone), so this reorder is safe for both paths.
        //   - Single appointments: null = floating/unresolved → skip SetOriginalTimeZone (no Win32 call).
        //   - Recurring appointments: MUST be non-null — SaveChanges() falls back to Win32
        //     TimeZoneInfo.Local when OriginalTimeZone is unset, breaking cross-platform builds.
        //     UTC is always a safe fallback for the blob's KeyName.
        TimeZoneInfo? zone = ResolveWindowsZone(a) ?? (a.Recurrence is null ? null : TimeZoneInfo.Utc);
        if (zone is not null)
            appt.SetOriginalTimeZone(zone);   // MUST precede SetStartAndDuration (RecurringAppointment ClipStart depends on the zone)

        // SetStartAndDuration writes PidLidAppointmentStartWhole / EndWhole + Clip + Common.
        int durationMinutes = (int)Math.Max(0, Math.Round((a.EndUtc - a.StartUtc).TotalMinutes));
        appt.SetStartAndDuration(a.StartUtc, durationMinutes);

        appt.IsAllDayEvent = a.IsAllDay;

        if (!string.IsNullOrEmpty(a.Location)) appt.Location = a.Location;
        appt.BusyStatus = (BusyStatus)busy;

        // PidLidPrivate (PSETID_Common 0x8506): coupled to Sensitivity==2 only.
        // Confidential (3) intentionally does NOT set it — matches Outlook ground truth (Task 0 dump).
        appt.IsPrivate = sensitivity == 2;

        // Static-tag props (same pattern as TaskWriter)
        appt.PC.SetInt32Property(PropertyID.PidTagImportance, importance);
        appt.PC.SetInt32Property(PropertyID.PidTagSensitivity, sensitivity);

        WriteBody(appt, a);
        WriteCategories(file, appt, a);
        WriteReminder(file, appt, a);

        // Recurring-specific: apply recurrence pattern, deletions, and overrides to the master.
        if (appt is RecurringAppointment ra)
        {
            ApplyRecurrence(ra, a.Recurrence!, durationMinutes);
            ApplyDeletions(ra, a, out var deletedDates);
            ApplyExceptions(ra, a, durationMinutes, (BusyStatus)busy, zone ?? TimeZoneInfo.Utc, deletedDates);
        }

        WriteAttendees(file, appt, a);   // recipients + organizer + meeting state (no-op when no attendees)

        appt.SaveChanges();
        folder.AddMessage(appt);
        // folder.SaveChanges() is the caller's responsibility (must be called before EndSavingChanges).
    }

    // -----------------------------------------------------------------------
    // Recurrence helpers (Task 3)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the Windows timezone for blob writing from <see cref="AppointmentRecord.OriginatingTimeZoneId"/>.
    /// Deterministic and silent — the mapper (Task 2) already warned on unmappable zones.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item><see cref="AppointmentRecord.OriginatingTimeZoneId"/> is null → return <see cref="AppointmentRecord.TimeZone"/> (may be null for floating events).</item>
    ///   <item>Id starts with <c>tzone://Microsoft/</c> → strip prefix, use remainder as Windows id.</item>
    ///   <item>Id equals "UTC" (case-insensitive) → <see cref="TimeZoneInfo.Utc"/>.</item>
    ///   <item>IANA id → <see cref="TimeZoneInfo.TryConvertIanaIdToWindowsId"/> → <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>.</item>
    ///   <item>Already-Windows id → <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>.</item>
    ///   <item>Any failure → <see cref="TimeZoneInfo.Utc"/> (last-resort safety guard, no warning).</item>
    /// </list>
    /// </summary>
    private static TimeZoneInfo? ResolveWindowsZone(AppointmentRecord a)
    {
        if (a.OriginatingTimeZoneId is null)
            return a.TimeZone;  // null = floating or unresolved → caller decides (recurring gets UTC fallback)

        string id = a.OriginatingTimeZoneId;

        // Strip tzone://Microsoft/ prefix → use remainder as Windows zone id
        const string TzonePrefix = "tzone://Microsoft/";
        if (id.StartsWith(TzonePrefix, StringComparison.Ordinal))
            id = id.Substring(TzonePrefix.Length);

        // UTC sentinel (case-insensitive to catch both "UTC" and "Utc" from Thunderbird)
        if (string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;

        // Try IANA → Windows id first (.NET 6+ TryConvertIanaIdToWindowsId works cross-platform via ICU)
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out string? winId) && winId is not null)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(winId); }
            catch { /* fall through */ }
        }

        // Try directly as a Windows/system id (also handles already-Windows ids from tzone:// prefix)
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { /* fall through */ }

        // Any failure → prefer the zone the mapper already resolved (e.g. the machine-local zone
        // anchored to a floating all-day event) so the recurrence/tz blob matches StartWhole;
        // UTC only as a last resort. (No warning — mapper already warned for a bogus id.)
        return a.TimeZone ?? TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Applies the recurrence pattern to the series master.
    /// Field mapping is ground-truthed against the Task 0 vendor blob tests
    /// (<see cref="Vendor.RecurringAppointmentBlobTests"/>).
    /// </summary>
    private static void ApplyRecurrence(RecurringAppointment ra, RecurrenceSpec s, int durationMinutes)
    {
        ra.RecurrenceType = s.Frequency switch
        {
            AppointmentRecurrenceFrequency.Daily      => RecurrenceType.EveryNDays,
            AppointmentRecurrenceFrequency.Weekly     => RecurrenceType.EveryNWeeks,
            AppointmentRecurrenceFrequency.Monthly    => RecurrenceType.EveryNMonths,
            AppointmentRecurrenceFrequency.MonthlyNth => RecurrenceType.EveryNthDayOfEveryNMonths,
            AppointmentRecurrenceFrequency.Yearly     => RecurrenceType.EveryNYears,
            AppointmentRecurrenceFrequency.YearlyNth  => RecurrenceType.EveryNthDayOfEveryNYears,
            _ => RecurrenceType.EveryNDays,
        };

        // Period: natural units (days, weeks, months, or years depending on frequency).
        // Vendored setter scales to the internal blob unit automatically.
        ra.Period = Math.Max(1, s.Interval);

        // Day: day-mask for weekly; OutlookDayOfWeek for nth-day patterns; day-of-month for others.
        ra.Day = s.Frequency switch
        {
            AppointmentRecurrenceFrequency.Weekly =>
                (int)ToMask(s.DaysOfWeek),
            AppointmentRecurrenceFrequency.MonthlyNth or AppointmentRecurrenceFrequency.YearlyNth =>
                (int)ToOutlookDay(s.DaysOfWeek.Length > 0 ? s.DaysOfWeek[0] : DayOfWeek.Monday),
            _ => s.DayOfMonth ?? s.FirstStartUtc.Day,
        };

        // DayOccurrenceNumber: only for Nth-day patterns (2nd Tuesday, Last Friday, etc.)
        if (s.Frequency is AppointmentRecurrenceFrequency.MonthlyNth or AppointmentRecurrenceFrequency.YearlyNth)
        {
            ra.DayOccurenceNumber = (s.NthOccurrence ?? 1) == -1
                ? DayOccurenceNumber.Last
                : (DayOccurenceNumber)Math.Clamp(s.NthOccurrence ?? 1, 1, 4);
        }

        // End-of-series: Count / Until → EndAfterNumberOfOccurrences + LastInstanceStartDate;
        //               NoEnd → sentinel year 4500.
        if ((s.EndKind is RecurrenceEndKind.Count or RecurrenceEndKind.Until)
            && s.LastInstanceStartUtc is { } last)
        {
            ra.EndAfterNumberOfOccurences = (s.EndKind == RecurrenceEndKind.Count);
            ra.LastInstanceStartDate = last;
        }
        else // NoEnd
        {
            ra.EndAfterNumberOfOccurences = false;
            ra.LastInstanceStartDate = new DateTime(4500, 8, 31, 0, 0, 0, DateTimeKind.Utc);
        }

        // Note: yearly Month is NOT set here — YearlyRecurrencePatternStructure has no Month field;
        // Outlook derives it from the start date via FirstDateTime. s.Month is for cardinality checks only.
    }

    // -----------------------------------------------------------------------
    // Deletion helpers (Task 4)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Populates <see cref="RecurringAppointment.DeletedInstanceDates"/> from
    /// <see cref="AppointmentRecord.DeletedOccurrences"/> (EXDATE lines).
    /// De-duplicates via <paramref name="deletedDates"/>: Task 5 passes the same set to
    /// <c>ApplyExceptions</c> so an EXDATE and an override on the same day don't create two entries.
    /// </summary>
    private static void ApplyDeletions(RecurringAppointment ra, AppointmentRecord a,
        out HashSet<DateTime> deletedDates)
    {
        deletedDates = new HashSet<DateTime>();
        foreach (var d in a.DeletedOccurrences)
            AddDeletedDate(ra, deletedDates, d.OriginalStartLocal.Date);
    }

    /// <summary>
    /// Adds a single zone-local day-start to <see cref="RecurringAppointment.DeletedInstanceDates"/>,
    /// guarded by <paramref name="seen"/> to prevent duplicates.
    /// The date is stored with <see cref="DateTimeKind.Unspecified"/> so the vendored serializer
    /// writes it verbatim as a zone-local wall-clock value (confirmed Task 0).
    /// </summary>
    private static void AddDeletedDate(RecurringAppointment ra, HashSet<DateTime> seen, DateTime localDate)
    {
        DateTime key = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        if (seen.Add(key)) ra.DeletedInstanceDates.Add(key);
    }

    // -----------------------------------------------------------------------
    // Exception helpers (Task 5)
    // -----------------------------------------------------------------------

    /// <summary>
    /// For each <see cref="AppointmentException"/> in <see cref="AppointmentRecord.Exceptions"/>:
    /// (a) writes a <c>method=5</c> embedded-message attachment via
    ///     <see cref="RecurringAppointment.AddModifiedInstanceAttachment"/>,
    /// (b) adds an <see cref="ExceptionInfoStructure"/> to the blob's <c>ExceptionList</c>, and
    /// (c) adds the original occurrence's zone-local date to the shared deleted-dates set
    ///     (count-equality invariant:
    ///     <c>ExceptionList.Count == ModifiedInstanceDates.Count</c>;
    ///     <c>ModifiedInstanceDates.Count ≤ DeletedInstanceDates.Count</c>).
    /// PR7a encodes Subject / StartEnd / Location / BusyStatus changes only; other changed fields
    /// are warned at MAP time (Task 2 <c>ToException</c>) and silently ignored here.
    /// </summary>
    private static void ApplyExceptions(RecurringAppointment ra, AppointmentRecord master,
        int masterDurationMinutes, BusyStatus masterBusy, TimeZoneInfo zone, HashSet<DateTime> deletedDates)
    {
        foreach (var ex in master.Exceptions)
        {
            DateTime origStartUtc = ex.OriginalInstance.OriginalStartUtc;
            DateTime newStartUtc = ex.NewStartUtc ?? origStartUtc;
            DateTime newEndUtc   = ex.NewEndUtc   ?? newStartUtc.AddMinutes(masterDurationMinutes);
            int newDuration = (int)Math.Max(0, Math.Round((newEndUtc - newStartUtc).TotalMinutes));
            BusyStatus busy = ex.BusyStatus is { } bs ? (BusyStatus)bs : masterBusy;

            ra.AddModifiedInstanceAttachment(origStartUtc, masterDurationMinutes, newStartUtc, newDuration,
                ex.Subject ?? master.Subject, ex.Location ?? "", busy, 0, MessagePriority.Normal, zone);

            var info = new ExceptionInfoStructure();
            info.SetOriginalStartDTUtc(origStartUtc, zone);
            info.SetStartAndDuration(newStartUtc, newDuration, zone);
            if (ex.ChangeFlags.HasFlag(AppointmentExceptionChangeFlags.Subject))  { info.HasModifiedSubject  = true; info.Subject  = ex.Subject  ?? master.Subject; }
            if (ex.ChangeFlags.HasFlag(AppointmentExceptionChangeFlags.Location)) { info.HasModifiedLocation = true; info.Location = ex.Location ?? ""; }
            if (ex.ChangeFlags.HasFlag(AppointmentExceptionChangeFlags.BusyStatus)) { info.HasModifiedBusyStatus = true; info.BusyStatus = busy; }
            ra.ExceptionList.Add(info);

            // Original date must also be a deleted instance (count-equality; de-dups shared set vs EXDATE).
            AddDeletedDate(ra, deletedDates, TimeZoneInfo.ConvertTimeFromUtc(origStartUtc, zone).Date);
        }
    }

    /// <summary>Converts a <see cref="DayOfWeek"/> array to a bitfield mask for weekly recurrence.</summary>
    /// <remarks>
    /// Maps DayOfWeek enum values to <see cref="DaysOfWeekFlags"/> bit positions:
    /// Sunday(0)→0x01, Monday(1)→0x02, …, Saturday(6)→0x40 — both enums share the same layout.
    /// </remarks>
    private static DaysOfWeekFlags ToMask(DayOfWeek[] days)
    {
        DaysOfWeekFlags m = 0;
        foreach (var d in days)
            m |= (DaysOfWeekFlags)(1u << (int)d);
        return m;
    }

    /// <summary>
    /// Converts a single <see cref="DayOfWeek"/> to the corresponding <see cref="OutlookDayOfWeek"/>
    /// value for Nth-day patterns (e.g., "2nd Tuesday").
    /// </summary>
    private static OutlookDayOfWeek ToOutlookDay(DayOfWeek d)
        => (OutlookDayOfWeek)(1u << (int)d);

    // -----------------------------------------------------------------------
    // Shared helpers (common fields — run for BOTH single and recurring paths)
    // -----------------------------------------------------------------------

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
    /// Works for both <see cref="SingleAppointment"/> and <see cref="RecurringAppointment"/>.
    /// </summary>
    private static void WriteAttendees(PSTFile file, Appointment appt, AppointmentRecord a)
    {
        // Promotes to a meeting ONLY when there is ≥1 non-organizer attendee. An organizer-only event
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
                RecipientType.To) { ResponseStatus = (int)AttendeeResponse.Organized });   // organizer copy = respOrganized

        foreach (var att in a.Attendees)   // each has a non-empty Email (mapper-enforced)
            recipients.Add(new MessageRecipient(att.DisplayName, att.Email, isOrganizer: false,
                MapRecipientType(att.Kind)) { ResponseStatus = (int)att.Response });

        appt.AddRecipients(recipients);

        // asfMeeting = 0x1 (PidLidAppointmentStateFlags, PSETID_Appointment 0x8217)
        const int AsfMeeting = 0x1;
        appt.StateFlags = AsfMeeting;

        // PidLidResponseStatus = respOrganized(1) — PR6 organizer-copy default (Task 0 recipe).
        // Only written when a.Organizer is known.
        if (a.Organizer is not null)
        {
            PropertyID rs = file.NameToIDMap.ObtainIDFromName(
                new PropertyName(PropertyLongID.PidLidResponseStatus, PropertySetGuid.PSETID_Appointment));
            appt.PC.SetInt32Property(rs, (int)AttendeeResponse.Organized);
        }
    }

    // WriteBody rules (ground-truthed from PstWriter.WriteMessage pattern):
    //   - If a.Body present → PidTagBody (plain text).
    //   - If a.BodyHtml present → PidTagHtml (UTF-8 bytes) + PidTagNativeBody=3 + InternetCodepage=65001;
    //     AND ensure PidTagBody exists — derive it from HTML via PstWriter.HtmlToPlainText if a.Body is empty.
    //   - PstWriter.HtmlToPlainText is internal-static in the same assembly (Mail2Pst.Core); no HtmlBody.cs needed.
    private static void WriteBody(Appointment appt, AppointmentRecord a)
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

    /// <summary>Writes Keywords (categories) MV-string if any are present.</summary>
    private static void WriteCategories(PSTFile file, Appointment appt, AppointmentRecord a)
    {
        if (a.Categories.Count > 0)
        {
            ushort kw = PropertyNameToIDMap.GetOrCreateStringNamedProperty(file, 2, "Keywords");
            appt.PC.SetMultiStringProperty((PropertyID)kw, a.Categories);
        }
    }

    /// <summary>
    /// Writes reminder props (PidLidReminderSet, PidLidReminderDelta, PidLidReminderSignalTime)
    /// if <see cref="AppointmentRecord.ReminderSet"/> is true.
    /// Appointments use a MINUTES-BEFORE DELTA (unlike tasks, which use an absolute time).
    /// </summary>
    private static void WriteReminder(PSTFile file, Appointment appt, AppointmentRecord a)
    {
        appt.IsReminderSet = a.ReminderSet;
        if (!a.ReminderSet) return;

        PropertyID deltaId = file.NameToIDMap.ObtainIDFromName(
            new PropertyName(PropertyLongID.PidLidReminderDelta, PropertySetGuid.PSETID_Common));
        appt.PC.SetInt32Property(deltaId, a.ReminderMinutesBefore);

        PropertyID signalId = file.NameToIDMap.ObtainIDFromName(
            new PropertyName(PropertyLongID.PidLidReminderSignalTime, PropertySetGuid.PSETID_Common));
        // Signal time = start − delta (UTC). This is the instant Outlook fires the reminder.
        DateTime signalTime = a.StartUtc.AddMinutes(-a.ReminderMinutesBefore);
        appt.PC.SetDateTimeProperty(signalId, signalTime);
    }
}

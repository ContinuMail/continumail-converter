// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Calendar;

public static class CalendarEventMapper
{
    // Mozilla calStorageCalendar flag bits (see calStorageCalendar.sys.mjs CAL_ITEM_FLAG):
    // 1=PRIVATE, 2=HAS_ATTENDEES, 4=HAS_PROPERTIES, 8=EVENT_ALLDAY, 16=HAS_RECURRENCE,
    // 32=HAS_EXCEPTIONS, 64=HAS_ATTACHMENTS, 128=HAS_RELATIONS, 256=HAS_ALARMS.
    // NOTE: HAS_PROPERTIES (4) is set on nearly every real event, so all-day MUST key on bit 8.
    private const int EventAllDay = 8;

    public static AppointmentRecord? Map(RawEventGroup group, out IReadOnlyList<string> warnings)
    {
        var w = new List<string>();
        warnings = w;

        RawEvent? master = group.Master;
        string title = master?.Title ?? "";

        // Group-skip rules (order matters)
        if (master is null)
        {
            w.Add("orphan event override (no master) skipped");
            return null;
        }
        // Defensive guard: a lone override mis-grouped as master (RECURRENCE-ID set on the master row).
        // This should not occur after correct grouping, but guard against it defensively.
        if (master.RecurrenceId is not null)
        {
            w.Add("event override row mis-grouped as master; skipped");
            return null;
        }

        var appt = new AppointmentRecord
        {
            Subject  = title,
            SourceId = master.Id ?? "",
        };

        // All-day flag
        bool isAllDay = (master.Flags & EventAllDay) != 0;
        appt.IsAllDay = isAllDay;

        // Timezone resolution — always resolve from EventStartTz
        var rz = TimeZoneResolver.Resolve(master.EventStartTz);

        if (rz.Warning is { } rzWarn)
            w.Add(rzWarn);

        // allDayFloating: an all-day event with no resolvable source zone. Anchored to the machine
        // LOCAL zone (local-midnight-in-UTC), NOT UTC — see the all-day computation below.
        bool allDayFloating = false;
        TimeZoneInfo? resolvedZone;
        if (rz.Zone != null && !rz.IsFloating)
        {
            resolvedZone   = rz.Zone;
            appt.TimeZone  = rz.Zone;
        }
        else if (isAllDay)
        {
            // All-day floating/unresolved: anchor midnight boundaries to the machine local zone and
            // carry that zone in the tz blob — this is how Outlook authors an all-day event. UTC would
            // place midnight at the wrong instant for any viewer east of UTC, straddling two calendar
            // days. (The resolver already warned for a bogus id; genuine floating is normal, so silent.)
            // See docs/research/2026-06-30-thunderbird-calendar-findings.md ("all-day is NOT floating").
            resolvedZone   = TimeZoneInfo.Local;
            appt.TimeZone  = TimeZoneInfo.Local;
            allDayFloating = true;
        }
        else
        {
            // Timed floating/unresolved — keep UTC instant, no display zone
            resolvedZone   = null;
            appt.TimeZone  = null;
            w.Add($"event '{title}': floating/unresolved timezone — stored as a fixed UTC instant");
        }

        // Start / End
        if (isAllDay)
        {
            // All-day: interpret the raw micros as a calendar date in resolvedZone,
            // then compute midnight boundaries.
            var startOffset = PrTime.FromMicros(master.EventStart);
            var endOffset   = PrTime.FromMicros(master.EventEnd);

            var tz = resolvedZone ?? TimeZoneInfo.Utc;

            // For a floating all-day event the raw micros ARE the intended wall-clock date; reading it
            // through the display zone would shift the date across the UTC boundary for negative-offset
            // zones. For a resolved zone, the stored instant is interpreted in that zone.
            DateTime AllDayDate(DateTime rawUtc) =>
                allDayFloating ? rawUtc.Date : TimeZoneInfo.ConvertTimeFromUtc(rawUtc, tz).Date;

            if (startOffset is null)
                w.Add($"all-day event '{title}': missing start — using sentinel date");
            var startLocalDate = AllDayDate(startOffset?.UtcDateTime ?? default);

            // Build local midnight
            var startLocalMidnight = new DateTime(
                startLocalDate.Year, startLocalDate.Month, startLocalDate.Day,
                0, 0, 0, DateTimeKind.Unspecified);

            appt.StartUtc = TimeZoneInfo.ConvertTimeToUtc(startLocalMidnight, tz);

            // Compute end local midnight
            DateTime? endLocalMidnight = null;
            if (endOffset is { } eo)
            {
                var endLocalDate = AllDayDate(eo.UtcDateTime);
                endLocalMidnight = new DateTime(
                    endLocalDate.Year, endLocalDate.Month, endLocalDate.Day,
                    0, 0, 0, DateTimeKind.Unspecified);
            }

            // If end <= start (in local) or end is null → one-day boundary (DST-aware: next local midnight in tz).
            if (endLocalMidnight is null || endLocalMidnight.Value <= startLocalMidnight)
            {
                var startLocalDate2 = TimeZoneInfo.ConvertTimeFromUtc(appt.StartUtc, tz).Date;
                var nextLocalMidnight = new DateTime(
                    startLocalDate2.Year, startLocalDate2.Month, startLocalDate2.Day,
                    0, 0, 0, DateTimeKind.Unspecified).AddDays(1);
                appt.EndUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, tz);
            }
            else
                appt.EndUtc = TimeZoneInfo.ConvertTimeToUtc(endLocalMidnight.Value, tz);
        }
        else
        {
            // Timed events
            appt.StartUtc = PrTime.FromMicros(master.EventStart)?.UtcDateTime ?? default;
            appt.EndUtc   = PrTime.FromMicros(master.EventEnd)?.UtcDateTime   ?? appt.StartUtc;

            if (appt.EndUtc < appt.StartUtc)
            {
                w.Add($"event '{title}': end precedes start — clamped to start");
                appt.EndUtc = appt.StartUtc;
            }
            else if (appt.EndUtc == appt.StartUtc)
            {
                w.Add($"event '{title}': zero-length event");
            }
        }

        // Preserve the originating timezone id verbatim (canonical IANA/Olson or tzone:// id from TB).
        appt.OriginatingTimeZoneId = master.EventStartTz;

        // Apply recurrence (RRULE/EXDATE) and exception overrides when present.
        if (master.Recurrence.Count > 0 || group.Overrides.Count > 0)
            ApplyRecurrence(appt, master, group, w);

        // Body (DESCRIPTION property)
        appt.Body = PropValue(master, "DESCRIPTION");

        // BodyHtml — ALTREP data:text/html on DESCRIPTION parameter
        var altrep = master.Parameters.FirstOrDefault(p =>
            string.Equals(p.Key1, "DESCRIPTION", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Key2, "ALTREP",      StringComparison.OrdinalIgnoreCase));

        if (altrep?.Value is { } altrepUri &&
            altrepUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
            IcalDataUri.TryDecode(altrepUri, out var altrepMediaType, out var altrepBytes) &&
            altrepMediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            appt.BodyHtml = Encoding.UTF8.GetString(altrepBytes);
        }

        // Location
        appt.Location = PropValue(master, "LOCATION");

        // Categories
        var cats = PropValue(master, "CATEGORIES");
        appt.Categories = cats is not null ? SplitCategories(cats) : Array.Empty<string>();

        // BusyStatus (explicit precedence)
        // 1. TENTATIVE status → 1 (Tentative)
        // 2. TRANSP=TRANSPARENT → 0 (Free)
        // 3. TRANSP=OPAQUE → 2 (Busy) — explicit wins over all-day default
        // 4. no explicit TRANSP → all-day defaults to 0 (Free); timed defaults to 2 (Busy)
        var transp = PropValue(master, "TRANSP");
        if (string.Equals(master.IcalStatus, "TENTATIVE", StringComparison.OrdinalIgnoreCase))
            appt.BusyStatus = 1;
        else if (string.Equals(transp, "TRANSPARENT", StringComparison.OrdinalIgnoreCase))
            appt.BusyStatus = 0;
        else if (string.Equals(transp, "OPAQUE", StringComparison.OrdinalIgnoreCase))
            appt.BusyStatus = 2;
        else
            appt.BusyStatus = isAllDay ? 0 : 2; // all-day events default to Free; timed events default to Busy

        // Importance from iCal PRIORITY
        appt.Importance = master.Priority switch
        {
            >= 1 and <= 4 => 2, // high
            >= 6 and <= 9 => 0, // low
            _             => 1, // normal (5 or null)
        };

        // Sensitivity from CLASS (Privacy column first, then CLASS property)
        var cls = master.Privacy ?? PropValue(master, "CLASS");
        appt.Sensitivity = cls?.ToUpperInvariant() switch
        {
            "PRIVATE"      => 2,
            "CONFIDENTIAL" => 3,
            _              => 0,
        };

        // Reminder — map first VALARM; warn if multiple alarms present
        if (master.Alarms.Count > 1)
            w.Add($"event '{title}': multiple Thunderbird alarms — only the first is converted");

        if (master.Alarms.Count > 0)
        {
            var rawBlock   = master.Alarms[0].IcalString ?? "";
            var alarmResult = ICalTextParser.ParseAlarm(rawBlock);

            foreach (var aw in alarmResult.Warnings)
                w.Add(aw);

            if (alarmResult.Value is { } alarm)
            {
                if (alarm.AbsoluteTimeUtc is { } absUtc)
                {
                    // Absolute trigger — minutes before StartUtc. A trigger at/after start would clamp to
                    // fire-at-start; drop it with a warning instead (symmetry with the relative path).
                    double minutesBefore = (appt.StartUtc - absUtc).TotalMinutes;
                    if (minutesBefore > 0)
                    {
                        appt.ReminderSet = true;
                        appt.ReminderMinutesBefore = (int)Math.Round(minutesBefore);
                    }
                    else
                    {
                        w.Add($"event '{title}': reminder fires at/after the event start — not converted");
                        AppendRawTriggerToBody(appt, rawBlock);
                    }
                }
                else if (alarm.RelativeOffset is { } offset)
                {
                    // Relative trigger — anchor by Related
                    DateTime anchor = alarm.Related switch
                    {
                        "END"   => appt.EndUtc,
                        _       => appt.StartUtc, // START or default
                    };

                    if (offset < TimeSpan.Zero)
                    {
                        // Negative offset — fires before the anchor; valid reminder. PidLidReminderDelta is
                        // minutes-before-START, so an END anchor must be re-expressed relative to start:
                        // delta = Start − (anchor + offset). For START this is just −offset (unchanged); for
                        // END on a timed event it can be negative (fires after start, before end) — the
                        // writer's PidLidReminderSignalTime = Start − delta still lands on the correct instant.
                        appt.ReminderSet = true;
                        DateTime signalTime = anchor + offset;
                        appt.ReminderMinutesBefore = (int)Math.Round((appt.StartUtc - signalTime).TotalMinutes);
                    }
                    else
                    {
                        // Zero or positive — fires at/after anchor; skip reminder
                        w.Add($"event '{title}': reminder fires at/after the anchor — not converted");
                        AppendRawTriggerToBody(appt, rawBlock);
                    }
                }
                else
                {
                    // Parsed but no usable trigger (e.g. a VALARM with no TRIGGER line); don't drop silently.
                    w.Add($"event '{title}': alarm has no usable trigger — not converted");
                    AppendRawTriggerToBody(appt, rawBlock);
                }
            }
            else
            {
                // Parse failed; warnings already added above; preserve raw trigger in body.
                AppendRawTriggerToBody(appt, rawBlock);
            }
        }

        // --- Attendees (after reminder block, before return) ---
        var attendeeLines = master.Attendees
            .Select(s => s.IcalString)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
        if (attendeeLines.Count > 0)
        {
            var parsed = ICalTextParser.ParseAttendees(attendeeLines);
            foreach (var warning in parsed.Warnings) w.Add($"event '{appt.Subject}': {warning}");

            var all = parsed.Value ?? Array.Empty<ParsedAttendee>();
            AppointmentAttendee? organizer = null;
            var attendees = new List<AppointmentAttendee>();
            // Dedup non-organizer attendees by normalized email (case-insensitive, first wins). Pre-seed with the
            // organizer's email so a duplicate ATTENDEE row for the organizer is dropped (common in CalDAV data).
            var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Organizer first (so its email pre-seeds the dedup set). Organizer MAY be display-only (no email);
            // the writer simply omits the organizer recipient row when the email is empty.
            foreach (ParsedAttendee p in all)
            {
                if (!p.IsOrganizer) continue;
                string email = (p.Email ?? "").Trim();
                string display = !string.IsNullOrWhiteSpace(p.CommonName) ? p.CommonName!.Trim() : email;
                if (display.Length == 0) break;                       // neither name nor email → no usable organizer
                organizer = new AppointmentAttendee
                {
                    DisplayName = display, Email = email,
                    Kind = AttendeeKind.Required, Response = AttendeeResponse.Organized, IsOrganizer = true,
                };
                if (email.Length > 0) seenEmails.Add(email);
                break;                                                // only one organizer
            }

            // Non-organizer attendees: REQUIRE an email (an Outlook recipient row with an empty address is invalid).
            foreach (ParsedAttendee p in all)
            {
                if (p.IsOrganizer) continue;
                string email = (p.Email ?? "").Trim();
                if (email.Length == 0)
                {
                    w.Add($"event '{appt.Subject}': attendee '{p.CommonName}' has no email — skipped");
                    continue;
                }
                if (!seenEmails.Add(email))                           // case-insensitive dedup (incl. vs the organizer)
                {
                    w.Add($"event '{appt.Subject}': duplicate attendee {email} skipped");
                    continue;
                }
                attendees.Add(new AppointmentAttendee
                {
                    DisplayName = !string.IsNullOrWhiteSpace(p.CommonName) ? p.CommonName!.Trim() : email,
                    Email = email,
                    Kind = MapKind(p.Role, p.CuType),
                    Response = MapResponse(p.ParticipationStatus, isOrganizer: false),
                });
            }

            appt.Organizer = organizer;
            appt.Attendees = attendees;
        }

        // --- GlobalObjectId (Exchange-cached events only) ---
        // For Exchange/Owl-cached events the id IS the GlobalObjectId, uppercase-hex-encoded
        // (112 chars = 56 bytes, EDK prefix 040000008200E00074C5B7101A82E008).
        // Mozilla UUIDs and CalDAV UIDs do not match; TryDecode returns false and GlobalObjectId stays null.
        if (GlobalObjectIdCodec.TryDecode(group.Master?.Id, out var goid, out _))
            appt.GlobalObjectId = goid;

        // --- Relations (cal_relations side-table: RELATED-TO etc.) ---
        // Classic Outlook has no native related-appointment surface; preserve raw relation lines
        // in the body appendix (via CalendarBodyAppendix in AppointmentWriter) + warn once per line.
        appt.Relations = master.Relations
            .Select(r => r.IcalString)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
        foreach (var rel in appt.Relations)
            w.Add($"relation on '{appt.Subject}': preserved (not natively converted)");

        // --- Online-meeting join URL (Teams / Google Meet) ---
        // Only these two provider X-props trigger URL preservation; ordinary URLs in
        // DESCRIPTION or LOCATION are not promoted (no over-promotion rule).
        var joinUrl =
            PropValue(master, "X-MICROSOFT-SKYPETEAMSMEETINGURL") ??
            PropValue(master, "X-GOOGLE-CONFERENCE");

        if (joinUrl is { Length: > 0 })
        {
            // Plain body: append if the URL is not already present in Body or Location.
            bool inBody     = appt.Body     is { } b && b.Contains(joinUrl, StringComparison.Ordinal);
            bool inLocation = appt.Location is { } l && l.Contains(joinUrl, StringComparison.Ordinal);

            if (!inBody && !inLocation)
                appt.Body = (appt.Body ?? "") + $"\nJoin online meeting: {joinUrl}";

            // HTML body: append an anchor block if BodyHtml is present and URL not already there.
            if (appt.BodyHtml is { Length: > 0 } html &&
                !html.Contains(joinUrl, StringComparison.Ordinal))
            {
                var escapedUrl   = HtmlEscape(joinUrl);
                var escapedLabel = HtmlEscape($"Join online meeting: {joinUrl}");
                appt.BodyHtml = html +
                    $"<p><a href=\"{escapedUrl}\">{escapedLabel}</a></p>";
            }
        }

        return appt;
    }

    // ---------------------------------------------------------------------------
    // Attendee mapping helpers
    // ---------------------------------------------------------------------------

    // CUTYPE wins (a room is a resource regardless of ROLE); else ROLE; default Required.
    private static AttendeeKind MapKind(string? role, string? cuType)
    {
        string c = (cuType ?? "").Trim().ToUpperInvariant();
        if (c is "ROOM" or "RESOURCE") return AttendeeKind.Resource;
        string r = (role ?? "").Trim().ToUpperInvariant();
        if (r is "OPT-PARTICIPANT" or "NON-PARTICIPANT") return AttendeeKind.Optional;
        return AttendeeKind.Required;
    }

    private static AttendeeResponse MapResponse(string? partStat, bool isOrganizer)
    {
        if (isOrganizer) return AttendeeResponse.Organized;
        return (partStat ?? "").Trim().ToUpperInvariant() switch
        {
            "ACCEPTED"     => AttendeeResponse.Accepted,
            "DECLINED"     => AttendeeResponse.Declined,
            "TENTATIVE"    => AttendeeResponse.Tentative,
            "NEEDS-ACTION" => AttendeeResponse.NotResponded,
            _              => AttendeeResponse.None,
        };
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string? PropValue(RawEvent ev, string key)
    {
        RawProperty? p = ev.Properties.FirstOrDefault(
            x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return p?.Value is { } b ? Encoding.UTF8.GetString(b) : null;
    }

    /// <summary>
    /// Minimal HTML escaping for placing text or a URL attribute value into an HTML fragment.
    /// Escapes &amp;, &lt;, and &gt; — sufficient for the join-URL anchor appended by the mapper.
    /// </summary>
    private static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Splits a CATEGORIES value on commas that are NOT escaped by a preceding backslash,
    /// then unescapes each token's \, sequences.  Trims whitespace and drops empty tokens
    /// and X-MOZ-* prefixed categories.
    /// </summary>
    private static IReadOnlyList<string> SplitCategories(string raw)
    {
        var parts  = Regex.Split(raw, @"(?<!\\),");
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var cat = part.Replace("\\,", ",").Trim();
            if (!string.IsNullOrEmpty(cat) &&
                !cat.StartsWith("X-MOZ-", StringComparison.OrdinalIgnoreCase))
                result.Add(cat);
        }
        return result;
    }

    /// <summary>
    /// Extracts the raw TRIGGER line from a VALARM block and appends a
    /// "not converted" notice to the appointment body.
    /// </summary>
    private static void AppendRawTriggerToBody(AppointmentRecord appt, string rawBlock)
    {
        string? triggerLine = null;
        foreach (var line in rawBlock.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (trimmed.StartsWith("TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                triggerLine = trimmed;
                break;
            }
        }
        if (triggerLine is not null)
            appt.Body = (appt.Body ?? "") + $"\n[Thunderbird alarm not converted: {triggerLine}]";
    }

    // ---------------------------------------------------------------------------
    // Recurrence helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Resolves a timezone id string to a <see cref="TimeZoneInfo"/> using the same
    /// rules as <see cref="TimeZoneResolver"/>. Falls back to UTC when unresolvable.
    /// </summary>
    private static TimeZoneInfo ResolveZone(string? tzId) =>
        TimeZoneResolver.Resolve(tzId).Zone ?? TimeZoneInfo.Utc;

    /// <summary>
    /// Converts a raw EXDATE date/datetime string to a <see cref="RecurrenceInstanceId"/>.
    /// For date-only values (<paramref name="isDateOnly"/>), the local midnight in
    /// <paramref name="tzId"/> is used as the reference point.
    /// </summary>
    // Returns null when the value cannot be parsed, so the caller skips that EXDATE with a warning
    // instead of emitting a default(0001-01-01) date that would break the vendored serializer.
    // internal for direct unit testing (via InternalsVisibleTo).
    internal static RecurrenceInstanceId? ToInstanceId(string value, string? tzId, bool isDateOnly)
    {
        var zone = ResolveZone(tzId);
        value = value?.Trim() ?? "";   // tolerate whitespace-padded EXDATE tokens

        if (isDateOnly)
        {
            if (DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            {
                var localMidnight = new DateTime(date.Year, date.Month, date.Day,
                    0, 0, 0, DateTimeKind.Unspecified);
                var utc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone);
                return new RecurrenceInstanceId(utc, localMidnight, tzId, IsDateOnly: true);
            }
            return null;
        }
        else
        {
            // yyyyMMddTHHmmssZ (UTC) or yyyyMMddTHHmmss (local in tzId zone)
            bool isUtc = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            string valueTrimmed = isUtc ? value.TrimEnd('z', 'Z') : value;

            if (DateTime.TryParseExact(valueTrimmed, "yyyyMMddTHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            {
                DateTime utc, local;
                if (isUtc)
                {
                    utc   = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    local = new DateTime(
                        TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Ticks,
                        DateTimeKind.Unspecified);
                }
                else
                {
                    local = new DateTime(parsed.Year, parsed.Month, parsed.Day,
                        parsed.Hour, parsed.Minute, parsed.Second, DateTimeKind.Unspecified);
                    utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                }
                return new RecurrenceInstanceId(utc, local, tzId, IsDateOnly: false);
            }
            return null;
        }
    }

    /// <summary>
    /// Maps a RECURRENCE-ID override <see cref="RawEvent"/> to an <see cref="AppointmentException"/>.
    /// Returns <c>null</c> when the override has no <c>RECURRENCE-ID</c> (should not happen after
    /// correct grouping, but guard defensively).
    /// Emits a warning into <paramref name="w"/> for fields that are not encoded by the writer in
    /// this release (Body, Reminder, Sensitivity, Categories).
    /// </summary>
    private static AppointmentException? ToException(RawEvent o, List<string> w)
    {
        if (o.RecurrenceId is null) return null;

        // Resolve the original-instance identity from the override's RECURRENCE-ID micros.
        var zone    = ResolveZone(o.RecurrenceIdTz);
        var utc     = PrTime.FromMicros(o.RecurrenceId)?.UtcDateTime ?? default;
        var localRaw = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var local   = new DateTime(localRaw.Year, localRaw.Month, localRaw.Day,
            localRaw.Hour, localRaw.Minute, localRaw.Second, DateTimeKind.Unspecified);
        var instanceId = new RecurrenceInstanceId(utc, local, o.RecurrenceIdTz, IsDateOnly: false);

        var flags  = AppointmentExceptionChangeFlags.None;
        string?  subject  = null;
        string?  location = null;
        string?  body     = null;
        int?     busyStatus = null;
        DateTime? newStart = null;
        DateTime? newEnd   = null;

        // Subject
        if (!string.IsNullOrEmpty(o.Title))
        {
            subject = o.Title;
            flags |= AppointmentExceptionChangeFlags.Subject;
        }

        // StartEnd
        if (o.EventStart.HasValue || o.EventEnd.HasValue)
        {
            newStart = PrTime.FromMicros(o.EventStart)?.UtcDateTime;
            newEnd   = PrTime.FromMicros(o.EventEnd)?.UtcDateTime;
            flags |= AppointmentExceptionChangeFlags.StartEnd;
        }

        // Location
        byte[]? locVal = o.Properties
            .FirstOrDefault(p => string.Equals(p.Key, "LOCATION", StringComparison.OrdinalIgnoreCase))?.Value;
        if (locVal is not null)
        {
            location = Encoding.UTF8.GetString(locVal);
            flags |= AppointmentExceptionChangeFlags.Location;
        }

        // Body (DESCRIPTION) — stored in model but not encoded by the writer; warn.
        byte[]? bodyVal = o.Properties
            .FirstOrDefault(p => string.Equals(p.Key, "DESCRIPTION", StringComparison.OrdinalIgnoreCase))?.Value;
        if (bodyVal is not null)
        {
            body = Encoding.UTF8.GetString(bodyVal);
            flags |= AppointmentExceptionChangeFlags.Body;
            w.Add($"exception for '{o.Title}': changed Body not encoded in this version; stored in model only");
        }

        // BusyStatus (from TENTATIVE status or TRANSP)
        string? transp = null;
        byte[]? transpVal = o.Properties
            .FirstOrDefault(p => string.Equals(p.Key, "TRANSP", StringComparison.OrdinalIgnoreCase))?.Value;
        if (transpVal is not null) transp = Encoding.UTF8.GetString(transpVal);

        if (string.Equals(o.IcalStatus, "TENTATIVE", StringComparison.OrdinalIgnoreCase))
        { busyStatus = 1; flags |= AppointmentExceptionChangeFlags.BusyStatus; }
        else if (string.Equals(transp, "TRANSPARENT", StringComparison.OrdinalIgnoreCase))
        { busyStatus = 0; flags |= AppointmentExceptionChangeFlags.BusyStatus; }
        else if (string.Equals(transp, "OPAQUE", StringComparison.OrdinalIgnoreCase))
        { busyStatus = 2; flags |= AppointmentExceptionChangeFlags.BusyStatus; }

        // Reminder — stored in model but not encoded by the writer; warn.
        if (o.Alarms.Count > 0)
        {
            flags |= AppointmentExceptionChangeFlags.Reminder;
            w.Add($"exception for '{o.Title}': changed Reminder not encoded in this version; stored in model only");
        }

        // Sensitivity — stored in model but not encoded by the writer; warn.
        if (!string.IsNullOrEmpty(o.Privacy))
        {
            flags |= AppointmentExceptionChangeFlags.Sensitivity;
            w.Add($"exception for '{o.Title}': changed Sensitivity not encoded in this version; stored in model only");
        }

        // Categories — stored in model but not encoded by the writer; warn.
        byte[]? catsVal = o.Properties
            .FirstOrDefault(p => string.Equals(p.Key, "CATEGORIES", StringComparison.OrdinalIgnoreCase))?.Value;
        if (catsVal is not null)
        {
            flags |= AppointmentExceptionChangeFlags.Categories;
            w.Add($"exception for '{o.Title}': changed Categories not encoded in this version; stored in model only");
        }

        return new AppointmentException
        {
            OriginalInstance       = instanceId,
            NewStartUtc            = newStart,
            NewEndUtc              = newEnd,
            Subject                = subject,
            Location               = location,
            Body                   = body,
            BusyStatus             = busyStatus,
            ChangeFlags            = flags,
        };
    }

    /// <summary>
    /// Applies RRULE/EXDATE recurrence data and override exceptions to <paramref name="appt"/>.
    /// Degrades to a single occurrence (no <c>Recurrence</c> set) with a warning when the rule
    /// cannot be faithfully mapped; a degraded result is still returned (never null).
    /// Spec-building is delegated to <see cref="RecurrenceMapping.FromIcal"/>;
    /// EXDATE/override mapping (appointment-only concerns) stays here.
    /// </summary>
    private static void ApplyRecurrence(
        AppointmentRecord appt, RawEvent master, RawEventGroup group, List<string> w)
    {
        var lines = master.Recurrence
            .Select(s => s.IcalString)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

        // Parse first to emit any parse warnings and to obtain p.ExDates for later use.
        ParseResult<ParsedRecurrence> pr = ICalTextParser.ParseRecurrence(lines);
        foreach (var pw in pr.Warnings) w.Add(pw);
        ParsedRecurrence? p = pr.Value;

        if (p is null)
        {
            // No mappable RRULE (parse failure or no RRULE line); event is a single occurrence.
            if (group.Overrides.Count > 0)
                w.Add($"recurrence: overrides dropped (no recurrence pattern) for '{appt.Subject}'");
            return;
        }

        DateTime firstStartLocal = appt.TimeZone is { } tz0
            ? TimeZoneInfo.ConvertTimeFromUtc(appt.StartUtc, tz0)
            : appt.StartUtc;

        var (spec, degradeReason) = RecurrenceMapping.FromIcal(
            lines, appt.StartUtc, firstStartLocal, appt.TimeZone, master.EventStartTz, p);   // reuse the parse above

        if (degradeReason is not null)
        {
            if (degradeReason == "COUNT enumeration failed")
                w.Add($"recurrence COUNT could not be resolved; wrote first occurrence only: '{appt.Subject}'");
            else
                w.Add($"recurrence unsupported ({degradeReason}); wrote first occurrence only: '{appt.Subject}'");
            return;  // appt.Recurrence remains null — degraded single occurrence
        }

        if (spec is null)
            return; // (null, null) — near-unreachable; p was non-null but FromIcal found nothing mappable

        appt.Recurrence = spec;

        // Map EXDATEs to deleted-occurrence identities (appointment-only; stays here).
        // An unparseable EXDATE is skipped with a warning rather than emitted as a default(0001-01-01).
        var deleted = new List<RecurrenceInstanceId>();
        foreach (var dl in p.ExDates)
            foreach (var v in dl.Values)
            {
                var id = ToInstanceId(v, dl.TzId ?? master.EventStartTz, dl.IsDateOnly);
                if (id is not null) deleted.Add(id);
                else w.Add($"event '{appt.Subject}': unparseable EXDATE '{v}' skipped");
            }
        appt.DeletedOccurrences = deleted;

        // Map override exceptions (appointment-only; stays here).
        appt.Exceptions = group.Overrides
            .Select(o => ToException(o, w))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }
}

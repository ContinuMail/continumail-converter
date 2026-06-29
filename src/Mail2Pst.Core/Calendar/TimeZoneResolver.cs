// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Core.Calendar;

/// <summary>Carries the result of resolving a Thunderbird timezone string.</summary>
/// <param name="Zone">Resolved <see cref="TimeZoneInfo"/>; null when unresolved or floating.</param>
/// <param name="IsFloating">True when the time has no timezone (all-day or legacy "floating").</param>
/// <param name="OriginalId">The raw string passed by the caller (inline VTIMEZONE block or plain id).</param>
/// <param name="ResolvedId">The id actually handed to <c>FindSystemTimeZoneById</c>; preserved even on failure
/// so callers can build a manual-mapping fallback.</param>
/// <param name="Warning">Non-null when the timezone was silently lost or unresolvable.</param>
public sealed record TimeZoneResolution(
    TimeZoneInfo? Zone,
    bool IsFloating,
    string? OriginalId,
    string? ResolvedId,
    string? Warning);

/// <summary>Turns a Thunderbird timezone string into a <see cref="TimeZoneResolution"/>.
/// Never throws — unresolvable ids yield a null Zone + Warning instead.</summary>
public static class TimeZoneResolver
{
    private const string VTimezonePrefix = "BEGIN:VTIMEZONE";
    private const string TzoneMsPrefix   = "tzone://Microsoft/";
    private const string NoTzDescription = "(no TZ description)";

    /// <summary>Resolves <paramref name="tz"/> using the Thunderbird timezone conventions.</summary>
    public static TimeZoneResolution Resolve(string? tz)
    {
        // 1. Null / empty / "floating" → floating, no warning.
        if (string.IsNullOrEmpty(tz) ||
            tz.Equals("floating", StringComparison.OrdinalIgnoreCase))
            return new TimeZoneResolution(null, IsFloating: true, OriginalId: tz, ResolvedId: null, Warning: null);

        // 2. Inline BEGIN:VTIMEZONE block — extract TZID and recurse, preserving OriginalId.
        if (tz.StartsWith(VTimezonePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string? extracted = ExtractTzId(tz);
            if (extracted is null)
                return new TimeZoneResolution(null, IsFloating: true, OriginalId: tz,
                    ResolvedId: null, Warning: "could not extract TZID from inline VTIMEZONE");
            var inner = Resolve(extracted);
            return inner with { OriginalId = tz };
        }

        // 3. "(no TZ description)" — floating but warn (silent tz loss is worse).
        if (tz == NoTzDescription)
            return new TimeZoneResolution(null, IsFloating: true, OriginalId: tz,
                ResolvedId: null, Warning: "no TZ description");

        // 4. UTC (case-insensitive).
        if (tz.Equals("UTC", StringComparison.OrdinalIgnoreCase))
            return new TimeZoneResolution(TimeZoneInfo.Utc, IsFloating: false, OriginalId: tz,
                ResolvedId: "UTC", Warning: null);

        // 5. tzone://Microsoft/<name>
        if (tz.StartsWith(TzoneMsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string name = tz[TzoneMsPrefix.Length..];
            if (name.Equals("Utc", StringComparison.OrdinalIgnoreCase))
                return new TimeZoneResolution(TimeZoneInfo.Utc, IsFloating: false, OriginalId: tz,
                    ResolvedId: "UTC", Warning: null);
            var zone = TryFind(name);
            return zone is not null
                ? new TimeZoneResolution(zone, IsFloating: false, OriginalId: tz, ResolvedId: zone.Id, Warning: null)
                : new TimeZoneResolution(null, IsFloating: false, OriginalId: tz,
                    ResolvedId: name, Warning: $"unresolved Microsoft tz '{name}'");
        }

        // 6. Olson / other.
        {
            var zone = TryFind(tz);
            return zone is not null
                ? new TimeZoneResolution(zone, IsFloating: false, OriginalId: tz, ResolvedId: zone.Id, Warning: null)
                : new TimeZoneResolution(null, IsFloating: false, OriginalId: tz,
                    ResolvedId: tz, Warning: $"unresolved tz '{tz}'");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Unfolds an iCal block and returns the value after the first TZID: line, or null.</summary>
    private static string? ExtractTzId(string block)
    {
        // RFC 5545 line unfolding: CRLF or LF followed by a single whitespace character is a fold.
        string unfolded = block
            .Replace("\r\n ", "")
            .Replace("\r\n\t", "")
            .Replace("\n ", "")
            .Replace("\n\t", "");

        foreach (string line in unfolded.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("TZID:", StringComparison.OrdinalIgnoreCase))
                return trimmed["TZID:".Length..];
        }
        return null;
    }

    /// <summary>Wraps <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> — returns null instead of throwing.</summary>
    private static TimeZoneInfo? TryFind(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
}

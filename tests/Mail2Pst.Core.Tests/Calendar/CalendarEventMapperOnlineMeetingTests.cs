// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Text;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

/// <summary>
/// Tests for online-meeting join-URL preservation in <see cref="CalendarEventMapper.Map"/>.
/// All data is synthetic/reserved (example.com, teams.example.com, meet.example.com) — no real mail or PII.
/// </summary>
public class CalendarEventMapperOnlineMeetingTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static RawEventGroup SimpleGroup(Action<RawEvent>? configure = null)
    {
        var ev = new RawEvent
        {
            Id           = "online-meeting-test@example.com",
            Title        = "Online Meeting",
            EventStart   = MicrosFor(2026, 8, 1, 14, 0),
            EventStartTz = "UTC",
            EventEnd     = MicrosFor(2026, 8, 1, 15, 0),
            EventEndTz   = "UTC",
            Flags        = 0,
            Priority     = 5,
        };
        configure?.Invoke(ev);
        return new RawEventGroup { Master = ev };
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int pos = 0;
        while ((pos = haystack.IndexOf(needle, pos, StringComparison.Ordinal)) >= 0)
        {
            count++;
            pos += needle.Length;
        }
        return count;
    }

    // -----------------------------------------------------------------------
    // Teams X-prop → Body contains URL
    // -----------------------------------------------------------------------

    [Fact]
    public void Teams_join_url_is_ensured_in_body()
    {
        const string joinUrl = "https://teams.example.com/l/x";

        var group = SimpleGroup(e =>
        {
            e.Properties.Add(new RawProperty("DESCRIPTION", Utf8("Meeting agenda."), null, null));
            e.Properties.Add(new RawProperty("X-MICROSOFT-SKYPETEAMSMEETINGURL", Utf8(joinUrl), null, null));
        });

        var rec = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(rec);
        Assert.Contains(joinUrl, rec!.Body ?? "");
    }

    // -----------------------------------------------------------------------
    // Google Meet X-prop → Body contains URL
    // -----------------------------------------------------------------------

    [Fact]
    public void Google_meet_join_url_is_ensured_in_body()
    {
        const string joinUrl = "https://meet.example.com/abc";

        var group = SimpleGroup(e =>
        {
            e.Properties.Add(new RawProperty("DESCRIPTION", Utf8("Stand-up notes."), null, null));
            e.Properties.Add(new RawProperty("X-GOOGLE-CONFERENCE", Utf8(joinUrl), null, null));
        });

        var rec = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(rec);
        Assert.Contains(joinUrl, rec!.Body ?? "");
    }

    // -----------------------------------------------------------------------
    // URL already in DESCRIPTION → not duplicated (appears exactly once)
    // -----------------------------------------------------------------------

    [Fact]
    public void Join_url_already_in_body_is_not_duplicated()
    {
        const string joinUrl = "https://teams.example.com/l/x";

        var group = SimpleGroup(e =>
        {
            // DESCRIPTION already contains the join URL.
            e.Properties.Add(new RawProperty("DESCRIPTION", Utf8($"Join: {joinUrl}"), null, null));
            e.Properties.Add(new RawProperty("X-MICROSOFT-SKYPETEAMSMEETINGURL", Utf8(joinUrl), null, null));
        });

        var rec = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(rec);
        Assert.Equal(1, CountOccurrences(rec!.Body ?? "", joinUrl));
    }

    // -----------------------------------------------------------------------
    // No provider X-prop → Body identical to DESCRIPTION (no synthetic append)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plain_appointment_body_unchanged_when_no_provider_xprop()
    {
        const string description = "Just a plain meeting with no online-meeting X-prop.";

        var group = SimpleGroup(e =>
        {
            e.Properties.Add(new RawProperty("DESCRIPTION", Utf8(description), null, null));
            // Intentionally no X-MICROSOFT-SKYPETEAMSMEETINGURL or X-GOOGLE-CONFERENCE.
            // A URL in DESCRIPTION alone must NOT trigger an append.
            e.Properties.Add(new RawProperty("LOCATION", Utf8("https://ordinary-url.example.com"), null, null));
        });

        var rec = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(rec);
        Assert.Equal(description, rec!.Body);
    }

    // -----------------------------------------------------------------------
    // Teams + BodyHtml (ALTREP) present → BodyHtml also contains URL
    // -----------------------------------------------------------------------

    [Fact]
    public void Teams_join_url_is_ensured_in_html_body_too()
    {
        const string joinUrl = "https://teams.example.com/l/x";

        // Build an ALTREP data URI with HTML that does NOT already contain the join URL.
        var html    = "<p>Meeting notes.</p>";
        var b64     = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        var dataUri = $"data:text/html;base64,{b64}";

        var group = SimpleGroup(e =>
        {
            e.Properties.Add(new RawProperty("DESCRIPTION", Utf8("Meeting notes."), null, null));
            e.Properties.Add(new RawProperty("X-MICROSOFT-SKYPETEAMSMEETINGURL", Utf8(joinUrl), null, null));
            e.Parameters.Add(new RawParameter("DESCRIPTION", "ALTREP", dataUri, null, null));
        });

        var rec = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(rec);
        Assert.Contains(joinUrl, rec!.BodyHtml ?? "");
    }
}

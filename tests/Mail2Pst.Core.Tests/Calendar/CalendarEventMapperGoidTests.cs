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
/// Tests that <see cref="CalendarEventMapper.Map"/> correctly wires <see cref="GlobalObjectIdCodec"/>
/// to set <see cref="AppointmentRecord.GlobalObjectId"/> for Exchange-cached events.
/// </summary>
public class CalendarEventMapperGoidTests
{
    // A 112-char Exchange GOID hex string (bytes 16-19 already zero).
    private const string ExchangeGoidHex =
        "040000008200E00074C5B7101A82E00800000000B40F44F359B6DC01000000000000000010000000DC21992D2F90224BB5EE6F8189627094";

    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;

    private static RawEventGroup SimpleGroup(string eventId)
    {
        var ev = new RawEvent
        {
            Id           = eventId,
            Title        = "Test Event",
            EventStart   = MicrosFor(2026, 8, 1, 10, 0),
            EventStartTz = "UTC",
            EventEnd     = MicrosFor(2026, 8, 1, 11, 0),
            EventEndTz   = "UTC",
            Flags        = 0,
            Priority     = 5,
        };
        return new RawEventGroup { Master = ev };
    }

    [Fact]
    public void Exchange_hex_event_id_sets_GlobalObjectId()
    {
        var group = SimpleGroup(ExchangeGoidHex);
        var rec = CalendarEventMapper.Map(group, out _);

        Assert.NotNull(rec);
        Assert.NotNull(rec!.GlobalObjectId);
        Assert.Equal(56, rec.GlobalObjectId!.Length);
        Assert.Equal(Convert.FromHexString(ExchangeGoidHex), rec.GlobalObjectId);
    }

    [Fact]
    public void Mozilla_uuid_event_id_does_not_set_GlobalObjectId()
    {
        var group = SimpleGroup("4ad842d0-1234-5678-9abc-def012345678");
        var rec = CalendarEventMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Null(rec!.GlobalObjectId);
        // No warning emitted for a non-Exchange id — it is normal, not an error.
        Assert.DoesNotContain(warns, w => w.Contains("GlobalObjectId", StringComparison.OrdinalIgnoreCase));
    }
}

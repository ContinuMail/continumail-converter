// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

/// <summary>
/// TDD tests for cal_relations preservation:
///   (a) mapper pure-unit — field set + warning emitted;
///   (b) writer round-trip — PidTagBody contains the appendix line.
/// All data is synthetic/reserved (example.com) — no real mail or PII.
/// </summary>
public class CalendarEventMapperRelationTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static long MicrosFor(int year, int month, int day, int hour = 0, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds() * 1000L;

    private static RawEventGroup SimpleGroup(Action<RawEvent>? configure = null)
    {
        var ev = new RawEvent
        {
            Id           = "relation-test-event@example.com",
            Title        = "Meeting With Relations",
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

    // -----------------------------------------------------------------------
    // Mapper unit tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Relations_are_collected_with_warning()
    {
        var group = SimpleGroup(e =>
            e.Relations.Add(new RawSideText("RELATED-TO;RELTYPE=PARENT:uid-123")));

        var rec = CalendarEventMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Contains("RELATED-TO;RELTYPE=PARENT:uid-123", rec!.Relations);
        Assert.Contains(warns, w => w.Contains("relation") && w.Contains("not natively converted"));
    }

    [Fact]
    public void Empty_relations_produce_no_warnings()
    {
        var group = SimpleGroup(); // no relations

        var rec = CalendarEventMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Empty(rec!.Relations);
        Assert.DoesNotContain(warns, w => w.Contains("relation") && w.Contains("not natively converted"));
    }

    [Fact]
    public void Multiple_relations_each_produce_a_warning()
    {
        var group = SimpleGroup(e =>
        {
            e.Relations.Add(new RawSideText("RELATED-TO;RELTYPE=PARENT:uid-A"));
            e.Relations.Add(new RawSideText("RELATED-TO;RELTYPE=CHILD:uid-B"));
        });

        var rec = CalendarEventMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Equal(2, rec!.Relations.Count);
        Assert.Equal(2, warns.Count(w => w.Contains("relation") && w.Contains("not natively converted")));
    }

    [Fact]
    public void Whitespace_only_relation_lines_are_dropped()
    {
        var group = SimpleGroup(e =>
        {
            e.Relations.Add(new RawSideText("   "));
            e.Relations.Add(new RawSideText("RELATED-TO:uid-real"));
        });

        var rec = CalendarEventMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Single(rec!.Relations);
        Assert.Equal("RELATED-TO:uid-real", rec.Relations[0]);
    }

    // -----------------------------------------------------------------------
    // Writer round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public void Writer_body_contains_relation_appendix()
    {
        var start = new DateTime(2026, 8, 1, 14, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc);

        var rec = new AppointmentRecord
        {
            Subject   = "Test Relation",
            StartUtc  = start,
            EndUtc    = end,
            Relations = new[] { "RELATED-TO;RELTYPE=PARENT:uid-123" },
        };

        string path = Path.Combine(Path.GetTempPath(), $"m2p-rel-{Guid.NewGuid():N}.pst");
        PSTFile? pst = null;
        try
        {
            PSTFile.CreateEmptyStore(path);

            try
            {
                pst = new PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
                pst.BeginSavingChanges();
                PSTFolder folder = pst.TopOfPersonalFolders.CreateChildFolder(
                    "Calendar", FolderItemTypeName.Appointment);
                new AppointmentWriter().WriteAppointment(pst, folder, rec);
                folder.SaveChanges();
                pst.EndSavingChanges();
            }
            finally { pst?.CloseFile(); pst = null; }

            try
            {
                pst = new PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
                PSTFolder found = pst.TopOfPersonalFolders.FindChildFolder("Calendar");
                CalendarFolder cal = Assert.IsType<CalendarFolder>(found);
                Appointment appt = cal.GetAppointment(0);
                string body = appt.PC.GetStringProperty(PropertyID.PidTagBody) ?? "";

                Assert.Contains("[Thunderbird relation not natively converted: RELATED-TO;RELTYPE=PARENT:uid-123]", body);
            }
            finally { pst?.CloseFile(); pst = null; }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

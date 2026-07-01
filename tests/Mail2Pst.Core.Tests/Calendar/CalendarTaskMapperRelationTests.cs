// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Linq;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

/// <summary>
/// TDD tests for cal_relations preservation in <see cref="CalendarTaskMapper.Map"/>.
/// All data is synthetic/reserved (example.com) — no real mail or PII.
/// </summary>
public class CalendarTaskMapperRelationTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RawTodoGroup SimpleGroup(Action<RawTodo>? configure = null)
    {
        var todo = new RawTodo
        {
            Id           = "task-relation-test@example.com",
            Title        = "Task With Relation",
            IcalStatus   = "NEEDS-ACTION",
            TodoComplete = 0,
            Priority     = 5,
        };
        configure?.Invoke(todo);
        return new RawTodoGroup { Master = todo };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Relations_are_collected_with_warning()
    {
        var group = SimpleGroup(t =>
            t.Relations.Add(new RawSideText("RELATED-TO;RELTYPE=PARENT:uid-123")));

        var rec = CalendarTaskMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Contains("RELATED-TO;RELTYPE=PARENT:uid-123", rec!.Relations);
        Assert.Contains(warns, w => w.Contains("relation") && w.Contains("not natively converted"));
    }

    [Fact]
    public void Empty_relations_produce_no_warnings()
    {
        var group = SimpleGroup(); // no relations

        var rec = CalendarTaskMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Empty(rec!.Relations);
        Assert.DoesNotContain(warns, w => w.Contains("relation") && w.Contains("not natively converted"));
    }

    [Fact]
    public void Multiple_relations_each_produce_a_warning()
    {
        var group = SimpleGroup(t =>
        {
            t.Relations.Add(new RawSideText("RELATED-TO;RELTYPE=PARENT:uid-A"));
            t.Relations.Add(new RawSideText("RELATED-TO;RELTYPE=CHILD:uid-B"));
        });

        var rec = CalendarTaskMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Equal(2, rec!.Relations.Count);
        Assert.Equal(2, warns.Count(w => w.Contains("relation") && w.Contains("not natively converted")));
    }

    [Fact]
    public void Whitespace_only_relation_lines_are_dropped()
    {
        var group = SimpleGroup(t =>
        {
            t.Relations.Add(new RawSideText("   "));
            t.Relations.Add(new RawSideText("RELATED-TO:uid-real"));
        });

        var rec = CalendarTaskMapper.Map(group, out var warns);

        Assert.NotNull(rec);
        Assert.Single(rec!.Relations);
        Assert.Equal("RELATED-TO:uid-real", rec.Relations[0]);
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkModelTests
{
    private static MorkTable Table(string id, string scope, string kind) =>
        new(id, scope, kind, new Dictionary<string, MorkRow>
        {
            ["1"] = new MorkRow("1", new Dictionary<string, string> { ["flags"] = "5" }),
        });

    [Fact]
    public void TryGetCell_PresentAndAbsent()
    {
        var row = new MorkRow("1", new Dictionary<string, string> { ["flags"] = "5" });
        Assert.True(row.TryGetCell("flags", out string v));
        Assert.Equal("5", v);
        Assert.False(row.TryGetCell("subject", out _));
    }

    [Fact]
    public void GetTables_ReturnsAllMatches_TryGetSingle_FalseOnMultiple()
    {
        var doc = new MorkDocument(new[]
        {
            Table("1", "scopeX", "kindX"),
            Table("2", "scopeX", "kindX"), // same scope+kind, different Id — must both survive
        });
        Assert.Equal(2, doc.GetTables("scopeX", "kindX").Count);
        Assert.False(doc.TryGetSingleTable("scopeX", "kindX", out _)); // ambiguous -> false
    }

    [Fact]
    public void TryGetSingleTable_TrueOnExactlyOne_FalseOnZero()
    {
        var doc = new MorkDocument(new[] { Table("1", "scopeX", "kindX") });
        Assert.True(doc.TryGetSingleTable("scopeX", "kindX", out MorkTable t));
        Assert.Equal("1", t.Id);
        Assert.False(doc.TryGetSingleTable("nope", "nope", out _));
    }

    [Fact]
    public void MorkRow_IsImmutableSnapshot()
    {
        var source = new Dictionary<string, string> { ["flags"] = "1" };
        var row = new MorkRow("1", source);
        source["flags"] = "9";                       // mutate the source after construction
        Assert.Equal("1", row.Cells["flags"]);       // snapshot unaffected
        Assert.IsNotType<Dictionary<string, string>>(row.Cells); // not a downcastable mutable dict
    }

    [Fact]
    public void MorkTable_RowsAreImmutableSnapshot()
    {
        var rows = new Dictionary<string, MorkRow> { ["1"] = new MorkRow("1", new Dictionary<string, string>()) };
        var table = new MorkTable("t", "s", "k", rows);
        rows.Clear();                                 // mutate the source after construction
        Assert.True(table.Rows.ContainsKey("1"));     // snapshot unaffected
        Assert.IsNotType<Dictionary<string, MorkRow>>(table.Rows);
    }
}

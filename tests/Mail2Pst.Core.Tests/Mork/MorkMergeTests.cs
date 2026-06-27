// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkMergeTests
{
    // Column dict: leading <(a=c)> marks this as the column atom space.
    private const string Dict = "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags)(81=subject) >\n";
    private static MorkRow Row(string src, string id) =>
        MorkReader.ParseString(Dict + src).TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out var t)
            ? t.Rows[id] : throw new System.Exception("table/row missing");

    [Fact]
    public void Merge_LaterUpdateOverwritesCell_LastWriteWins()
    {
        // row 1 flags=1, then a later group updates flags=5 -> final 5; subject from first write retained
        var r = Row("{1:^80 {(k^96:c)} [1(^88=1)(^81=hi)] } @$${1{@ {1:^80 [1(^88=5)] } @$$}1}@", "1");
        Assert.Equal("5", r.Cells["flags"]);
        Assert.Equal("hi", r.Cells["subject"]); // untouched cell retained
    }

    [Fact]
    public void Merge_RowCut_RemovesRow()
    {
        var doc = MorkReader.ParseString(Dict + "{1:^80 {(k^96:c)} [1(^88=1)] } @$${1{@ {1:^80 [-1] } @$$}1}@");
        Assert.True(doc.TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out var t));
        Assert.False(t.Rows.ContainsKey("1"));
    }

    [Fact]
    public void Merge_DeleteThenReAdd_RowPresentWithReAddedCells()
    {
        var doc = MorkReader.ParseString(Dict + "{1:^80 {(k^96:c)} [1(^88=1)] } @$${1{@ {1:^80 [-1] } @$$}1}@ @$${2{@ {1:^80 [1(^88=9)] } @$$}2}@");
        Assert.True(doc.TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out var t));
        Assert.True(t.Rows.ContainsKey("1"));
        Assert.Equal("9", t.Rows["1"].Cells["flags"]);
    }

    [Fact]
    public void Merge_TableLevelCutMarker_FoldsIntoBaseTable_NotPhantomTable()
    {
        // KB-003: a transaction restating the table with a leading '-' (`{-1:^80 ...}`) is a Mork
        // table-level cut/transaction marker that targets table 1 — NOT a declaration of a new table
        // "-1". Real Thunderbird .msf emits this during a folder reparse. The assembler must fold it
        // into table 1 (yielding ONE msgs table), not fork a phantom second msgs table — which made
        // MsfMessageReader throw "found 2 msgs tables".
        var doc = MorkReader.ParseString(
            Dict + "{1:^80 {(k^96:c)} [1(^88=1)] } @$${1{@ {-1:^80 {(k^96:c)} [1(^88=5)] } @$$}1}@");
        // Exactly one msgs-scoped table — TryGetSingleTable returns false if a phantom "-1" also exists.
        Assert.True(doc.TryGetSingleTable(
            "ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out var t));
        Assert.Equal("5", t.Rows["1"].Cells["flags"]); // transaction folded into table 1 (last-write-wins)
    }

    [Fact]
    public void Merge_RowUpdateToEmptyValue_CellPresentButEmpty()
    {
        // Task 0 RESOLVED: Thunderbird .msf has NO cell-cut. `(^81=)` is an EMPTY-STRING value applied
        // by a normal row update — the cell stays PRESENT with value "". Deletion is row-level only
        // (`[-id]`). (121,093 such empty cells in the real corpus, all in normal non-cut rows.)
        var r = Row("{1:^80 {(k^96:c)} [1(^88=1)(^81=hi)] } @$${1{@ {1:^80 [1(^81=)] } @$$}1}@", "1");
        Assert.Equal("1", r.Cells["flags"]);   // untouched cell retained
        Assert.Equal("", r.Cells["subject"]);  // overwritten to empty string (NOT removed)
    }
}

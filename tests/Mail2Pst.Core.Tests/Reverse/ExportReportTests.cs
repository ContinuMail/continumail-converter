// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Text.Json;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class ExportReportTests
{
    [Fact]
    public void Complete_Warnings_And_Skips_PopulateReport()
    {
        var report = new ExportReport(@"C:\out");
        report.RecordWarning("lossy prop dropped");
        report.RecordSkipped(new ExportSkip("Inbox", 3, "System.IO.InvalidDataException: bad node"));
        report.Complete(
            new MboxTreeWriteResult(new[] { @"C:\out\Inbox", @"C:\out\Sent" }, FoldersWritten: 2, MessagesWritten: 5),
            TimeSpan.FromMilliseconds(1234));

        Assert.Equal(@"C:\out", report.OutputRoot);
        Assert.Equal(5, report.MessagesExported);
        Assert.Equal(2, report.FoldersWritten);
        Assert.Equal(2, report.MboxFiles.Count);
        Assert.Equal(1, report.WarningCount);
        Assert.Contains("lossy prop dropped", report.Warnings);
        Assert.Equal(1, report.SkippedCount);
        Assert.Equal("Inbox", report.Skipped[0].FolderPath);
        Assert.Equal(3, report.Skipped[0].MessageIndex);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), report.Elapsed);
    }

    [Fact]
    public void ToJson_EmitsCamelCaseShape_WithSkippedAndCounts()
    {
        var report = new ExportReport(@"C:\out");
        report.RecordWarning("w1");
        report.RecordSkipped(new ExportSkip("Sent", null, "unreadable"));
        report.Complete(new MboxTreeWriteResult(new[] { @"C:\out\Inbox" }, 1, 3), TimeSpan.FromMilliseconds(50));

        using JsonDocument doc = JsonDocument.Parse(report.ToJson());
        JsonElement root = doc.RootElement;
        Assert.Equal(3, root.GetProperty("messagesExported").GetInt32());
        Assert.Equal(1, root.GetProperty("foldersWritten").GetInt32());
        Assert.Equal(50, root.GetProperty("elapsedMs").GetInt64());
        Assert.Equal(1, root.GetProperty("skippedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("warningCount").GetInt32());
        Assert.Equal("Sent", root.GetProperty("skipped")[0].GetProperty("folderPath").GetString());
        Assert.Equal("w1", root.GetProperty("warnings")[0].GetString());
        Assert.Equal(@"C:\out\Inbox", root.GetProperty("mboxFiles")[0].GetString());
    }

    [Fact]
    public void ToSummary_ListsCountsWarningsAndSkips_NullIndexHasNoEmptyBrackets()
    {
        var report = new ExportReport(@"C:\out");
        report.RecordWarning("dropped X");
        report.RecordSkipped(new ExportSkip("Inbox", 7, "boom"));
        report.RecordSkipped(new ExportSkip("Sent", null, "folder-level"));
        report.Complete(new MboxTreeWriteResult(Array.Empty<string>(), 0, 0), TimeSpan.Zero);

        string s = report.ToSummary();
        Assert.Contains("Messages exported: 0", s);
        Assert.Contains("Skipped: 2", s);
        Assert.Contains("Inbox[7]", s);
        Assert.Contains("SKIP Sent: folder-level", s);   // null index -> no "[]"
        Assert.DoesNotContain("Sent[]", s);
        Assert.Contains("Warnings: 1", s);
        Assert.Contains("dropped X", s);
    }
}

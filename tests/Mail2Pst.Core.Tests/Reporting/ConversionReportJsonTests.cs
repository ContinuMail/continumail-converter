// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text.Json;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Xunit;

namespace Mail2Pst.Core.Tests.Reporting;

public class ConversionReportJsonTests
{
    [Fact]
    public void ToJson_EmptyReport_SerializesCountsCorrectly()
    {
        var report = new ConversionReport();
        report.RecordConverted();
        report.RecordConverted();

        string json = report.ToJson();
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("converted").GetInt32());
        Assert.Equal(0, root.GetProperty("skipped").GetArrayLength());
        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void ToJson_WithAllItemTypes_SerializesContactAppointmentTaskCounters()
    {
        var report = new ConversionReport();
        // mail
        report.RecordConverted();
        // contacts
        report.RecordContactConverted();
        report.RecordContactConverted();
        report.RecordContactSkipped("abook.sqlite", "unresolvable name");
        // appointments
        report.RecordAppointmentConverted();
        report.RecordAppointmentConverted();
        report.RecordAppointmentConverted();
        // tasks
        report.RecordTaskConverted();

        string json = report.ToJson();
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("converted").GetInt32());
        Assert.Equal(2, root.GetProperty("contactsConverted").GetInt32());
        Assert.Equal(1, root.GetProperty("contactsSkipped").GetInt32());
        Assert.Equal(3, root.GetProperty("appointmentsConverted").GetInt32());
        Assert.Equal(1, root.GetProperty("tasksConverted").GetInt32());
    }

    [Fact]
    public void ToJson_WithSkipAndWarning_SerializesEntries()
    {
        var report = new ConversionReport();
        report.RecordConverted();
        report.RecordSkipped(new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #5" }, "bad encoding");
        report.RecordWarning(new SourceReference { SourcePath = "Inbox.mbox", Identifier = "message #3" }, "dropped attachment");

        string json = report.ToJson();
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("converted").GetInt32());
        Assert.Equal(1, root.GetProperty("skipped").GetArrayLength());
        Assert.Equal(1, root.GetProperty("warnings").GetArrayLength());

        JsonElement skip = root.GetProperty("skipped")[0];
        Assert.Equal("Inbox.mbox", skip.GetProperty("source").GetString());
        Assert.Equal("message #5", skip.GetProperty("identifier").GetString());
        Assert.Equal("bad encoding", skip.GetProperty("reason").GetString());

        JsonElement warning = root.GetProperty("warnings")[0];
        Assert.Equal("Inbox.mbox", warning.GetProperty("source").GetString());
        Assert.Equal("message #3", warning.GetProperty("identifier").GetString());
        Assert.Equal("dropped attachment", warning.GetProperty("reason").GetString());
    }
}

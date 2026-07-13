// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

using System.Text.Json;
using Mail2Pst.Cli;                    // ExportCommand (internal, visible via InternalsVisibleTo)
using Mail2Pst.Core.Cli;              // CliEventSerializer
using Mail2Pst.Core.Reverse;          // ExportSkip / ExportProgress
using Xunit;

namespace Mail2Pst.Integration.Tests.Reverse;

// Pure unit proof that the CLI builds DISCRIMINATED, disjoint skip vs warning events — the exact wire
// shape of the `skipped` event type, which no PST-scenario test can force without a corrupt store. The
// reader→onSkipped link is covered by PstMailReaderSkipTests; the runner clean-path by PstExportRunnerTests.
public class ExportEventBuilderTests
{
    private static JsonElement Serialize(object payload)
    {
        using JsonDocument doc = JsonDocument.Parse(CliEventSerializer.Serialize(payload));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void BuildSkippedEvent_IsDiscriminatedSkip_NotAWarning()
    {
        JsonElement e = Serialize(ExportCommand.BuildSkippedEvent(new ExportSkip("Parent / Inbox", 4, "InvalidDataException: bad node")));
        Assert.Equal("skipped", e.GetProperty("type").GetString());
        Assert.Equal("export", e.GetProperty("command").GetString());
        Assert.Equal("Parent / Inbox", e.GetProperty("folderPath").GetString());
        Assert.Equal(4, e.GetProperty("messageIndex").GetInt32());
        Assert.Equal("InvalidDataException: bad node", e.GetProperty("reason").GetString());
        Assert.False(e.TryGetProperty("message", out _));   // NOT a warning
    }

    [Fact]
    public void BuildWarningEvent_IsDiscriminatedWarning_NotASkip()
    {
        JsonElement e = Serialize(ExportCommand.BuildWarningEvent("reconstructor warning"));
        Assert.Equal("warning", e.GetProperty("type").GetString());
        Assert.Equal("export", e.GetProperty("command").GetString());
        Assert.Equal("reconstructor warning", e.GetProperty("message").GetString());
        Assert.False(e.TryGetProperty("folderPath", out _));   // NOT a skip
        Assert.False(e.TryGetProperty("reason", out _));
    }

    [Fact]
    public void BuildSkippedEvent_NullMessageIndex_SerializesJsonNull()
    {
        JsonElement e = Serialize(ExportCommand.BuildSkippedEvent(new ExportSkip("Contacts", null, "non-mail folder")));
        Assert.Equal(JsonValueKind.Null, e.GetProperty("messageIndex").ValueKind);
    }
}

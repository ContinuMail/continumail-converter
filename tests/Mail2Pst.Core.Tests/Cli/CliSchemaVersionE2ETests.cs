// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

// End-to-end guard for the CLI contract: EVERY JSON-Lines event the CLI emits on
// stdout must carry an integer `schemaVersion`. Unit tests already cover
// CliEventSerializer in isolation; this spawns the real built CLI so a future
// emission site that bypasses the serializer would be caught.
public class CliSchemaVersionE2ETests
{
    private static (int exitCode, List<string> jsonLines) RunCli(string args)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{CliE2EProcess.CliDllPath()}\" {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = CliE2EProcess.RepoRoot(),
        };

        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        _ = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);

        var lines = new List<string>();
        foreach (string raw in stdout.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return (proc.ExitCode, lines);
    }

    private static void AssertEveryLineCarriesSchemaVersion(List<string> jsonLines)
    {
        Assert.NotEmpty(jsonLines);
        foreach (string line in jsonLines)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            Assert.True(
                doc.RootElement.TryGetProperty("schemaVersion", out JsonElement sv),
                $"event missing schemaVersion: {line}");
            Assert.Equal(JsonValueKind.Number, sv.ValueKind);
            Assert.True(sv.GetInt32() >= 1, $"schemaVersion must be >= 1: {line}");
        }
    }

    [Fact]
    public void Version_EventCarriesSchemaVersion()
    {
        (int exit, List<string> lines) = RunCli("version");
        Assert.Equal(0, exit);
        AssertEveryLineCarriesSchemaVersion(lines);
    }

    [Fact]
    public void Scan_StreamingEventsCarrySchemaVersion()
    {
        // --progress forces compact JSON-Lines (the default scan prints one pretty,
        // multi-line object, which is not line-delimited).
        (int exit, List<string> lines) = RunCli("scan --progress --input fixtures/sample.mbox");
        Assert.Equal(0, exit);
        AssertEveryLineCarriesSchemaVersion(lines);
    }

    [Fact]
    public void Convert_EveryStreamedEventCarriesSchemaVersion()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-cli-e2e-" + Guid.NewGuid());
        try
        {
            (int exit, List<string> lines) = RunCli($"convert --config fixtures/sample-config.json --output \"{outDir}\"");
            Assert.Equal(0, exit);
            AssertEveryLineCarriesSchemaVersion(lines);
            // Sanity: the stream actually reached its terminal success event.
            Assert.Contains(lines, l => l.Contains("\"type\":\"done\""));
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, true);
            }
        }
    }
}

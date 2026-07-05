// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

// Spawns the real built CLI to exercise the `outlook-profiles list` command wiring end-to-end
// (registry read via WindowsRegistryKeyReader, OutlookDetection.ClassicOutlookAvailable, the JSON
// projection, CliEventSerializer). Windows-only: the registry hive the command reads doesn't exist
// on other platforms. Mirrors the spawn pattern in DiscoverCommandE2ETests / CliSchemaVersionE2ETests.
public class OutlookProfilesE2ETests
{
    private static (int exitCode, string stdout, string stderr) RunCli(string args)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{CliE2EProcess.CliDllPath()}\" {args}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = CliE2EProcess.RepoRoot(),
        };
        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return (proc.ExitCode, stdout, stderr);
    }

    [SkippableFact]
    public void List_EmitsOutlookProfilesShape_WithSchemaVersion()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "outlook-profiles reads the Windows registry.");

        (int exit, string stdout, string stderr) = RunCli("outlook-profiles list");
        Assert.True(exit == 0, $"expected exit 0, got {exit}. stderr: {stderr}");

        using JsonDocument doc = JsonDocument.Parse(stdout);
        JsonElement root = doc.RootElement;
        Assert.Equal("outlookProfiles", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("classicOutlook", out JsonElement classicOutlook));
        Assert.True(classicOutlook.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.True(root.TryGetProperty("profiles", out JsonElement profiles));
        Assert.Equal(JsonValueKind.Array, profiles.ValueKind);
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        // No assertion on `defaultProfile`, `classicOutlook` value, or `profiles` contents —
        // those are machine-specific facts, not part of the wiring contract this test checks.
    }
}

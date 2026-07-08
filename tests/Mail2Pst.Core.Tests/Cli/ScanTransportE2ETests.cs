// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mail2Pst.TestSupport;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

/// <summary>
/// Process-level tests for the scan --input-list transport: spawns the REAL built CLI
/// (same boundary the desktop sidecar crosses) against generated profiles with
/// hostile-but-legal path shapes and the long-path contract pair. Scale coverage lives in
/// the permanent #66 regression (400 folders, Integration.Tests) — no separate scale
/// ladder: the transport is a line-based file read with no plausible failure mode between
/// N and 4N lines.
/// </summary>
public class ScanTransportE2ETests
{
    // Async reads + real timeout: sequential ReadToEnd() before WaitForExit() can deadlock
    // (child blocks writing a full stderr pipe while we block reading stdout), and would
    // let a hung child block the test forever before any timeout fires.
    private static async Task<(int exit, string stdout, string stderr)> RunScanProcessAsync(IEnumerable<string> inputPaths)
    {
        string listPath = Path.Combine(Path.GetTempPath(), "m2p-e2e-list-" + Guid.NewGuid() + ".txt");
        File.WriteAllLines(listPath, inputPaths);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(CliE2EProcess.CliDllPath());
            psi.ArgumentList.Add("scan");
            psi.ArgumentList.Add("--input-list");
            psi.ArgumentList.Add(listPath);

            using Process proc = Process.Start(psi)!;
            Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Assert.Fail("scan process hung past 120s");
            }

            return (proc.ExitCode, await stdoutTask, await stderrTask);
        }
        finally
        {
            File.Delete(listPath);
        }
    }

    private static int SourcesOf(string stdout)
    {
        using JsonDocument doc = JsonDocument.Parse(stdout);
        return doc.RootElement.GetProperty("totals").GetProperty("sources").GetInt32();
    }

    // Tier 1 — hostile-but-legal folder names travel the whole pipe: generator → list file
    // → process argv-free transport → JSON result.
    [Fact]
    public async Task Scan_HostilePathShapes_AllScanCleanly()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("has space", 1)
            .WithFolder("hash#tag", 1)
            .WithFolder("amp&ersand", 1)
            .WithFolder("paren(these)s", 1)
            .WithFolder("Færdige opgaver", 1)     // Danish
            .WithFolder("งานที่เสร็จแล้ว", 1)        // Thai
            .Build();

        (int exit, string stdout, string stderr) = await RunScanProcessAsync(profile.MailFilePaths);

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        Assert.Equal(6, SourcesOf(stdout));
    }

    // Tier 1 — C4(a): a deep-but-legal path (each segment normal, total comfortably below
    // classic MAX_PATH) must scan like any other. Prefix length is DERIVED from the actual
    // temp root so the total lands ~220-240 on every machine/runner, never "add N and hope".
    [Fact]
    public async Task Scan_DeepPathBelowClassicMaxPath_Succeeds()
    {
        int prefixChars = Math.Max(0, 200 - Path.GetTempPath().Length);
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithDeepRoot(approximatePrefixChars: prefixChars)
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("INBOX", 1)
            .Build();
        Assert.All(profile.MailFilePaths, p => Assert.InRange(p.Length, 0, 259));

        (int exit, string stdout, string stderr) = await RunScanProcessAsync(profile.MailFilePaths);

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        Assert.Equal(1, SourcesOf(stdout));
    }

    // Tier 1 — C4(b): a path above 260 chars. Contract: if this environment can create and
    // read it, scan succeeds; if the OS/runtime refuses, the CLI fails CLEANLY — nonzero
    // exit, a one-line error that NAMES the offending path — never an unhandled-exception
    // dump, never a hang.
    [Fact]
    public async Task Scan_PathAboveClassicMaxPath_SucceedsOrFailsCleanly()
    {
        GeneratedProfile profile;
        try
        {
            profile = new ThunderbirdProfileBuilder()
                .WithDeepRoot(approximatePrefixChars: 300)
                .WithAccount("alice@example.com", "imap.example.com")
                .WithFolder("INBOX", 1)
                .Build();
        }
        catch (IOException)
        {
            return; // environment can't even express the path — nothing to contract-test
        }
        using (profile)
        {
            Assert.Contains(profile.MailFilePaths, p => p.Length > 260);

            (int exit, string stdout, string stderr) = await RunScanProcessAsync(profile.MailFilePaths);

            if (exit == 0)
            {
                Assert.Equal(1, SourcesOf(stdout));
            }
            else
            {
                Assert.Equal(1, exit);
                // The error must name the offending path (the folder file is "INBOX"),
                // not be a vague "failed" — and must not be a crash dump.
                Assert.Contains("INBOX", stderr);
                Assert.DoesNotContain("Unhandled exception", stderr);
                Assert.DoesNotContain("   at ", stderr); // no stack trace
            }
        }
    }
}

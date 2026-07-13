// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

// End-to-end guard for the reverse `export` subcommand: spawns the real built CLI so dispatch,
// stdout encoding, arg validation, PstExportRunner wiring, and the JSON-Lines stream are exercised
// together. The happy path first runs `convert` to produce a PST (no committed PST fixture needed),
// then runs `export` against it — the vendored PST read happens inside the spawned CLI process.
public class ExportCommandE2ETests
{
    // Concurrent async reads + an enforced timeout: reading one stream to end while the child fills
    // the other can deadlock, and Process.WaitForExit(ms) does NOT bound ReadToEnd(). Start both reads
    // first, await exit under a CancellationTokenSource, kill the tree AND drain the exit on timeout.
    private static async Task<(int exit, string stdout, string stderr)> RunCliAsync(params string[] cliArgs)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = CliE2EProcess.RepoRoot(),
        };
        psi.ArgumentList.Add(CliE2EProcess.CliDllPath());
        foreach (string a in cliArgs) psi.ArgumentList.Add(a);

        using Process proc = Process.Start(psi)!;
        Task<string> outTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> errTask = proc.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { await proc.WaitForExitAsync(); } catch { /* best-effort drain so the read tasks detach */ }
            throw new TimeoutException("CLI process did not exit within 2 minutes.");
        }

        string stdout = await outTask;
        string stderr = await errTask;
        return (proc.ExitCode, stdout, stderr);
    }

    // Parse each JSON line ONCE, clone the root, dispose the doc — so the returned elements outlive
    // their JsonDocument and there is no repeated deferred-LINQ reparse.
    private static JsonElement ParseObject(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        return doc.RootElement.Clone();
    }

    private static JsonElement[] Events(string stdout) =>
        stdout.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(ParseObject).ToArray();

    private static string Type(JsonElement e) => e.GetProperty("type").GetString()!;

    // Run `convert` on the committed sample fixture; return the first output PST path.
    private static async Task<string> ConvertSampleToPstAsync(string outDir)
    {
        (int exit, string stdout, _) = await RunCliAsync(
            "convert", "--config", "fixtures/sample-config.json", "--output", outDir);
        Assert.Equal(0, exit);
        JsonElement done = Events(stdout).Single(e => Type(e) == "done");
        string pst = done.GetProperty("outputs")[0].GetString()!;
        Assert.True(File.Exists(pst), $"expected a produced PST at {pst}");
        return pst;
    }

    [Fact]
    public async Task Export_NoArgs_EmitsOnlyValidationError_NoStarted_ExitOne()
    {
        (int exit, string stdout, _) = await RunCliAsync("export");

        Assert.Equal(1, exit);
        JsonElement[] events = Events(stdout);
        JsonElement err = Assert.Single(events);                 // the error is the ONLY stdout event
        Assert.Equal("error", Type(err));
        Assert.Equal("export", err.GetProperty("command").GetString());
        Assert.Equal("export", err.GetProperty("stage").GetString());
        Assert.True(err.GetProperty("fatal").GetBoolean());
        Assert.Equal(1, err.GetProperty("schemaVersion").GetInt32());
        Assert.DoesNotContain(events, e => Type(e) == "started");
    }

    [Fact]
    public async Task Export_MissingInputFile_EmitsOnlyValidationError_NoStarted_ExitOne()
    {
        string missing = Path.Combine(Path.GetTempPath(), "m2p-nope-" + Guid.NewGuid() + ".pst");
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());

        (int exit, string stdout, _) = await RunCliAsync("export", "--input", missing, "--output", outDir);

        Assert.Equal(1, exit);
        JsonElement[] events = Events(stdout);
        JsonElement err = Assert.Single(events);
        Assert.Equal("error", Type(err));
        Assert.Equal("export", err.GetProperty("command").GetString());
        Assert.DoesNotContain(events, e => Type(e) == "started");
        Assert.False(Directory.Exists(outDir));                  // no tree, no sibling report created
        Assert.False(File.Exists(outDir + ".export-report.json"));
    }

    [Fact]
    public async Task Export_ForwardWrittenPst_StreamsStartedProgressDone()
    {
        string pstDir = Path.Combine(Path.GetTempPath(), "m2p-export-pst-" + Guid.NewGuid());
        string mboxDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());
        try
        {
            string pst = await ConvertSampleToPstAsync(pstDir);

            (int exit, string stdout, _) = await RunCliAsync("export", "--input", pst, "--output", mboxDir);

            Assert.Equal(0, exit);
            JsonElement[] events = Events(stdout);

            // Contract invariants: schemaVersion + command discriminator on EVERY export event.
            foreach (JsonElement e in events)
            {
                Assert.Equal(1, e.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("export", e.GetProperty("command").GetString());
            }

            string[] types = events.Select(Type).ToArray();
            Assert.Equal("started", types[0]);
            Assert.False(events[0].GetProperty("includeEmpty").GetBoolean());  // flag defaults off

            Assert.Contains("progress", types);                               // sample has ≥1 message
            string[] terminals = types.Where(t => t is "done" or "error" or "cancelled").ToArray();
            Assert.Single(terminals);
            Assert.Equal("done", types[^1]);                                  // terminal is the LAST event

            JsonElement done = events.Single(e => Type(e) == "done");
            Assert.True(done.GetProperty("messagesExported").GetInt32() >= 1);
            Assert.True(done.GetProperty("foldersWritten").GetInt32() >= 1);
            Assert.True(done.GetProperty("mboxFileCount").GetInt32() >= 1);
            Assert.Equal(1, done.GetProperty("outputs").GetArrayLength());     // bounded: root only

            // Live event counts match the terminal report counts — locks the disjoint warning/skip semantics.
            Assert.Equal(events.Count(e => Type(e) == "warning"), done.GetProperty("warnings").GetInt32());
            Assert.Equal(events.Count(e => Type(e) == "skipped"), done.GetProperty("skipped").GetInt32());
        }
        finally
        {
            if (Directory.Exists(pstDir)) Directory.Delete(pstDir, true);
            if (Directory.Exists(mboxDir)) Directory.Delete(mboxDir, true);
        }
    }

    [Fact]
    public async Task Export_WritesReportFiles_AsSiblings_WithMatchingElapsed()
    {
        string pstDir = Path.Combine(Path.GetTempPath(), "m2p-export-pst-" + Guid.NewGuid());
        string mboxDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());
        try
        {
            string pst = await ConvertSampleToPstAsync(pstDir);
            (int exit, string stdout, _) = await RunCliAsync("export", "--input", pst, "--output", mboxDir);
            Assert.Equal(0, exit);

            string reportJson = mboxDir + ".export-report.json";     // SIBLING, not inside the tree
            string reportTxt = mboxDir + ".export-report.txt";
            Assert.True(File.Exists(reportJson), $"expected sibling {reportJson}");
            Assert.True(File.Exists(reportTxt), $"expected sibling {reportTxt}");
            Assert.False(File.Exists(Path.Combine(mboxDir, "export-report.json")), "report must NOT be inside the tree");

            JsonElement done = Events(stdout).Single(e => Type(e) == "done");
            Assert.Equal(reportJson, done.GetProperty("report").GetString());

            using JsonDocument reportDoc = JsonDocument.Parse(File.ReadAllText(reportJson));
            Assert.Equal(done.GetProperty("messagesExported").GetInt32(),
                         reportDoc.RootElement.GetProperty("messagesExported").GetInt32());
            Assert.Equal(done.GetProperty("elapsedMs").GetInt64(),
                         reportDoc.RootElement.GetProperty("elapsedMs").GetInt64());
        }
        finally
        {
            if (Directory.Exists(pstDir)) Directory.Delete(pstDir, true);
            if (Directory.Exists(mboxDir)) Directory.Delete(mboxDir, true);
            foreach (string s in new[] { mboxDir + ".export-report.json", mboxDir + ".export-report.txt" })
                if (File.Exists(s)) File.Delete(s);
        }
    }

    [Fact]
    public async Task Export_ReportWriteFails_PreservesPublishedTree_EmitsReportStageError()
    {
        string pstDir = Path.Combine(Path.GetTempPath(), "m2p-export-pst-" + Guid.NewGuid());
        string mboxDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());
        string blockingDir = mboxDir + ".export-report.json";        // a DIRECTORY where the report file must go
        try
        {
            string pst = await ConvertSampleToPstAsync(pstDir);
            Directory.CreateDirectory(blockingDir);                  // report publish is refused up front

            (int exit, string stdout, _) = await RunCliAsync("export", "--input", pst, "--output", mboxDir);

            Assert.NotEqual(0, exit);
            JsonElement[] events = Events(stdout);
            Assert.DoesNotContain(events, e => Type(e) == "done");   // never claims success
            Assert.Equal("error", events.Select(Type).Last());       // terminal is the error
            JsonElement err = Assert.Single(events.Where(e => Type(e) == "error"));
            Assert.Equal("export", err.GetProperty("command").GetString());
            Assert.Equal("report", err.GetProperty("stage").GetString());
            Assert.True(err.GetProperty("fatal").GetBoolean());
            Assert.True(err.GetProperty("outputPreserved").GetBoolean());
            Assert.True(Directory.Exists(mboxDir), "the published mbox tree must survive a report failure");
            Assert.NotEmpty(Directory.GetFileSystemEntries(mboxDir));
        }
        finally
        {
            if (Directory.Exists(pstDir)) Directory.Delete(pstDir, true);
            if (Directory.Exists(mboxDir)) Directory.Delete(mboxDir, true);
            if (Directory.Exists(blockingDir)) Directory.Delete(blockingDir, true);
            if (File.Exists(mboxDir + ".export-report.txt")) File.Delete(mboxDir + ".export-report.txt");
        }
    }

    [Fact]
    public async Task Export_SecondReportPublishFails_RollsBackFirstReport()
    {
        string pstDir = Path.Combine(Path.GetTempPath(), "m2p-export-pst-" + Guid.NewGuid());
        string mboxDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());
        string blockingTxtDir = mboxDir + ".export-report.txt";      // .txt destination is occupied
        try
        {
            string pst = await ConvertSampleToPstAsync(pstDir);
            Directory.CreateDirectory(blockingTxtDir);

            (int exit, string stdout, _) = await RunCliAsync("export", "--input", pst, "--output", mboxDir);

            Assert.NotEqual(0, exit);
            JsonElement err = Assert.Single(Events(stdout).Where(e => Type(e) == "error"));
            Assert.Equal("report", err.GetProperty("stage").GetString());
            // The JSON report must NOT be left published, and no temp files may remain (roll back / never publish).
            Assert.False(File.Exists(mboxDir + ".export-report.json"), "the first report file must not be left behind");
            string parent = Path.GetDirectoryName(mboxDir)!;
            Assert.Empty(Directory.GetFiles(parent, Path.GetFileName(mboxDir) + "*.tmp-*"));
            Assert.True(Directory.Exists(mboxDir), "the published mbox tree must survive");
        }
        finally
        {
            if (Directory.Exists(pstDir)) Directory.Delete(pstDir, true);
            if (Directory.Exists(mboxDir)) Directory.Delete(mboxDir, true);
            if (Directory.Exists(blockingTxtDir)) Directory.Delete(blockingTxtDir, true);
            if (File.Exists(mboxDir + ".export-report.json")) File.Delete(mboxDir + ".export-report.json");
        }
    }

    [Fact]
    public async Task Export_UnreadablePst_EmitsStartedThenSingleFatalError_ExitOne_NoReport()
    {
        // A file that EXISTS (passes the File.Exists guard) but is not a valid PST — the runner opens it
        // during pass 1 and throws; the CLI must turn that into ONE fatal error after `started`, exit 1.
        string junkPst = Path.Combine(Path.GetTempPath(), "m2p-export-junk-" + Guid.NewGuid() + ".pst");
        string mboxDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());
        File.WriteAllText(junkPst, "this is not a pst file");
        try
        {
            (int exit, string stdout, _) = await RunCliAsync("export", "--input", junkPst, "--output", mboxDir);

            Assert.Equal(1, exit);
            string[] types = Events(stdout).Select(Type).ToArray();
            Assert.Equal("started", types[0]);                       // started ALWAYS precedes the open
            Assert.Equal("error", types[^1]);                        // terminal error is the LAST event
            Assert.Equal(1, types.Count(t => t == "error"));
            Assert.DoesNotContain("done", types);

            JsonElement err = Events(stdout).Single(e => Type(e) == "error");
            Assert.Equal("export", err.GetProperty("command").GetString());
            Assert.Equal("export", err.GetProperty("stage").GetString());
            Assert.True(err.GetProperty("fatal").GetBoolean());

            // A pre-publication failure leaves no tree and no sibling report.
            Assert.False(Directory.Exists(mboxDir));
            Assert.False(File.Exists(mboxDir + ".export-report.json"));
        }
        finally
        {
            if (File.Exists(junkPst)) File.Delete(junkPst);
            if (Directory.Exists(mboxDir)) Directory.Delete(mboxDir, true);
        }
    }

    [Fact]
    public async Task Export_PreExistingReportFile_IsRejectedWithoutOverwritingIt()
    {
        string pstDir = Path.Combine(Path.GetTempPath(), "m2p-export-pst-" + Guid.NewGuid());
        string mboxDir = Path.Combine(Path.GetTempPath(), "m2p-export-tree-" + Guid.NewGuid());
        string existingReport = mboxDir + ".export-report.json";
        try
        {
            string pst = await ConvertSampleToPstAsync(pstDir);
            File.WriteAllText(existingReport, "ORIGINAL");           // a pre-existing sibling the user owns

            (int exit, string stdout, _) = await RunCliAsync("export", "--input", pst, "--output", mboxDir);

            Assert.NotEqual(0, exit);
            JsonElement err = Assert.Single(Events(stdout).Where(e => Type(e) == "error"));
            Assert.Equal("report", err.GetProperty("stage").GetString());
            Assert.Equal("ORIGINAL", File.ReadAllText(existingReport)); // NOT overwritten
            Assert.True(Directory.Exists(mboxDir), "the published mbox tree must survive");
        }
        finally
        {
            if (Directory.Exists(pstDir)) Directory.Delete(pstDir, true);
            if (Directory.Exists(mboxDir)) Directory.Delete(mboxDir, true);
            if (File.Exists(existingReport)) File.Delete(existingReport);
            if (File.Exists(mboxDir + ".export-report.txt")) File.Delete(mboxDir + ".export-report.txt");
        }
    }
}

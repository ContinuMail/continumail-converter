// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

// End-to-end guard for the `convert` CANCEL contract the GUI depends on. The
// writer-level cancel→delete-in-progress-part path is unit-tested
// (PstWriterCancellationTests); this pins the CLI process behaviour: a `cancel`
// line on stdin mid-write must produce the terminal `cancelled` event (with
// deleted[]/outputs[]), exit code 2, NO `done`, and NO success report on disk.
public class ConvertCancelE2ETests
{
    // Large enough that a 500-message checkpoint (where cancellation is observed)
    // lands well before the write finishes, with bodies sized so writing takes
    // longer than the stdin cancel round-trip. Cancel is sent on the first
    // `progress` event (writing underway → the in-progress part exists).
    private const int MessageCount = 5000;

    private static string WriteLargeMbox(int count)
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-cancel-" + Guid.NewGuid() + ".mbox");
        var body = new string('x', 400);
        var sb = new StringBuilder(count * 600);
        for (int i = 0; i < count; i++)
        {
            sb.Append("From s").Append(i).Append("@example.com Mon Jan 01 00:00:00 2024\r\n");
            sb.Append("From: s").Append(i).Append("@example.com\r\n");
            sb.Append("Subject: Message ").Append(i).Append("\r\n");
            sb.Append("Date: Mon, 01 Jan 2024 00:00:00 +0000\r\n\r\n");
            sb.Append(body).Append("\r\n\r\n");
        }
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    [Fact]
    public void Convert_CancelOnStdinMidWrite_EmitsCancelled_DeletesPart_Exit2_NoDoneNoReport()
    {
        string mbox = WriteLargeMbox(MessageCount);
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-cancel-out-" + Guid.NewGuid());
        string config = Path.Combine(Path.GetTempPath(), "m2p-cancel-cfg-" + Guid.NewGuid() + ".json");
        // maxSizeMB generous so the run produces a single part (no split) — the
        // cancelled in-progress part is then deleted with no completed parts left.
        string json =
            "{\"outputs\":[{\"name\":\"Personal\",\"maxSizeMB\":1024,\"folderMapping\":\"mirror\"," +
            "\"sources\":[{\"path\":" + JsonSerializer.Serialize(mbox) + ",\"type\":\"mbox\"}]}]}";
        File.WriteAllText(config, json);

        try
        {
            var psi = new ProcessStartInfo("dotnet",
                $"\"{CliE2EProcess.CliDllPath()}\" convert --config \"{config}\" --output \"{outDir}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = CliE2EProcess.RepoRoot(),
            };

            using Process proc = Process.Start(psi)!;
            // Drain stderr on a thread so a full stderr buffer can never deadlock the run.
            var stderr = new StringBuilder();
            var errThread = new Thread(() => { try { stderr.Append(proc.StandardError.ReadToEnd()); } catch { } })
            { IsBackground = true };
            errThread.Start();

            var lines = new List<string>();
            bool cancelSent = false;
            string? raw;
            while ((raw = proc.StandardOutput.ReadLine()) is not null)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                lines.Add(line);
                if (!cancelSent && line.Contains("\"type\":\"progress\""))
                {
                    proc.StandardInput.WriteLine("cancel");
                    proc.StandardInput.Flush();
                    cancelSent = true;
                }
            }

            Assert.True(proc.WaitForExit(120_000), "convert did not exit in time");
            errThread.Join(5_000);

            Assert.True(cancelSent, "no `progress` event observed — the test never exercised a mid-write cancel");
            Assert.Equal(2, proc.ExitCode);

            // Terminal event is `cancelled`, never `done` (mutually exclusive).
            string? cancelledLine = lines.Find(l => l.Contains("\"type\":\"cancelled\""));
            Assert.True(cancelledLine is not null, $"no `cancelled` event. stderr: {stderr}");
            Assert.DoesNotContain(lines, l => l.Contains("\"type\":\"done\""));

            using JsonDocument doc = JsonDocument.Parse(cancelledLine!);
            JsonElement root = doc.RootElement;
            Assert.Equal("cancelled", root.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Array, root.GetProperty("deleted").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("outputs").ValueKind);
            // The in-progress single part was deleted; no completed parts survive.
            Assert.True(root.GetProperty("deleted").GetArrayLength() >= 1,
                "cancelled must report the deleted in-progress part");
            Assert.Equal(0, root.GetProperty("outputs").GetArrayLength());

            // No success report written on cancel, and the in-progress .pst is gone from disk.
            Assert.False(File.Exists(Path.Combine(outDir, "conversion-report.json")),
                "success report must not be written when cancelled");
            Assert.True(!Directory.Exists(outDir) || Directory.GetFiles(outDir, "*.pst").Length == 0,
                "in-progress .pst must be deleted on cancel");
        }
        finally
        {
            if (File.Exists(mbox)) File.Delete(mbox);
            if (File.Exists(config)) File.Delete(config);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        }
    }
}

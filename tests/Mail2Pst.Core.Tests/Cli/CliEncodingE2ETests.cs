// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

// Spawns the real built CLI and reads RAW stdout bytes (not a decoded TextWriter) to prove a
// non-ASCII folder name survives intact through the CLI's stdout end to end: Console.OutputEncoding
// (Program.cs) writes UTF-8 bytes for the process's stdout stream, CliEventSerializer emits the
// standard \u-escaped JSON (unchanged wire format), and a spec-compliant JSON parser (here,
// System.Text.Json, mirroring the GUI's serde parser) decodes it back to the original character.
// This is a round-trip/regression test, not a raw-byte mojibake test: pre-fix (no
// Console.OutputEncoding set), a lone non-ASCII byte written through a legacy OEM/ANSI console
// codepage can corrupt the UTF-8 byte stream before JSON parsing ever sees it, which this test
// would catch via the U+FFFD replacement-char check. Mirrors the spawn/RepoRoot/CliDllPath pattern
// in DiscoverCommandE2ETests.
public class CliEncodingE2ETests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mail2Pst.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mail2Pst.sln).");
    }

    private static string CliDllPath()
    {
        string config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/bin/Release/") ? "Release" : "Debug";
        string dll = Path.Combine(RepoRoot(), "src", "Mail2Pst.Cli", "bin", config, "net8.0", "Mail2Pst.Cli.dll");
        Assert.True(File.Exists(dll), $"CLI build output not found at {dll}");
        return dll;
    }

    // Reads stdout as raw bytes so we observe the exact encoding the process wrote.
    private static (int exitCode, byte[] stdout, string stderr) RunCliRaw(string args)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{CliDllPath()}\" {args}")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = RepoRoot(),
        };
        using Process proc = Process.Start(psi)!;
        using var buffer = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(buffer);   // raw bytes, no decoding
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60_000);
        return (proc.ExitCode, buffer.ToArray(), stderr);
    }

    [Fact]
    public void Cli_EmitsFolderNameWithUmlaut_StdoutBytesDecodeAsUtf8_NoReplacementChar()
    {
        string dir = Path.Combine(Path.GetTempPath(), "m2p-enc-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string mbox = Path.Combine(dir, "Kläranlage.mbox");
        File.WriteAllText(mbox, "From a@b Mon Jan  1 00:00:00 2020\r\n\r\nx\r\n", new UTF8Encoding(false));
        try
        {
            (int exit, byte[] stdout, string stderr) = RunCliRaw($"scan --input \"{mbox}\"");
            Assert.True(exit == 0, $"expected exit 0, got {exit}. stderr: {stderr}");

            // UTF-8 decode of the raw bytes with replacement detection: no U+FFFD. Pre-fix
            // (Windows legacy codepage) the 'ä' is a lone 0xE4 byte, which UTF-8 decoding replaces
            // with U+FFFD -- corrupting the JSON text before it is even parsed. (Not
            // throwOnInvalid: we detect the failure via the U+FFFD assertion below rather than an
            // exception.)
            string text = new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(stdout);
            Assert.DoesNotContain('�', text);

            // The wire format itself is unchanged: System.Text.Json's default encoder still
            // \u-escapes non-ASCII (e.g. "Kläranlage"), so parse the JSON and assert on the
            // DECODED value -- proving the umlaut round-trips through the CLI's stdout and a
            // spec-compliant JSON parser, not that raw UTF-8 bytes appear literally on the wire.
            using JsonDocument doc = JsonDocument.Parse(text);
            string? displayName = doc.RootElement.GetProperty("sources")[0].GetProperty("displayName").GetString();
            Assert.Contains("Kläranlage", displayName);
        }
        finally { Directory.Delete(dir, true); }
    }
}

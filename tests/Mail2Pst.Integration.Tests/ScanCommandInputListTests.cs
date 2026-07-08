// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text.Json;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// `scan --input-list &lt;file&gt;` reads input paths (one per line) from a file, so
/// callers with hundreds of sources stay under the Windows 32,767-char command-line
/// limit (GitHub issue #66). The flag is additive and combines with repeated --input.
/// </summary>
[Collection("ConsoleCapture")]
public class ScanCommandInputListTests
{
    private const string MboxMessage =
        "From sender@example.com Mon Jan 01 00:00:00 2024\r\n" +
        "Message-ID: <input-list-test@example.com>\r\n" +
        "Subject: Test\r\n" +
        "\r\n" +
        "Body.\r\n";

    private static string WriteMbox(string dir, string name)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, MboxMessage);
        return path;
    }

    private static (int exit, string stdout, string stderr) RunScan(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        TextWriter originalOut = Console.Out;
        TextWriter originalErr = Console.Error;
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            int exit = ScanCommand.Run(args);
            return (exit, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Scan_InputListFile_ScansAllListedPaths_IgnoringBlankLines()
    {
        string root = Path.Combine(Path.GetTempPath(), "m2p-scan-inputlist-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string a = WriteMbox(root, "a.mbox");
            string b = WriteMbox(root, "b.mbox");
            string listPath = Path.Combine(root, "inputs.txt");
            File.WriteAllLines(listPath, new[] { a, "", "   ", b });

            (int exit, string stdout, _) = RunScan("scan", "--input-list", listPath);

            Assert.Equal(0, exit);
            using JsonDocument doc = JsonDocument.Parse(stdout);
            Assert.Equal(2, doc.RootElement.GetProperty("totals").GetProperty("sources").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_InputListCombinesWithRepeatedInputFlags()
    {
        string root = Path.Combine(Path.GetTempPath(), "m2p-scan-inputlist-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string a = WriteMbox(root, "a.mbox");
            string b = WriteMbox(root, "b.mbox");
            string listPath = Path.Combine(root, "inputs.txt");
            File.WriteAllLines(listPath, new[] { b });

            (int exit, string stdout, _) = RunScan("scan", "--input", a, "--input-list", listPath);

            Assert.Equal(0, exit);
            using JsonDocument doc = JsonDocument.Parse(stdout);
            Assert.Equal(2, doc.RootElement.GetProperty("totals").GetProperty("sources").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_MissingInputListFile_FailsWithMessage()
    {
        string listPath = Path.Combine(Path.GetTempPath(), "m2p-missing-" + Guid.NewGuid() + ".txt");

        (int exit, _, string stderr) = RunScan("scan", "--input-list", listPath);

        Assert.Equal(1, exit);
        Assert.Contains("Input list file not found", stderr);
    }

    [Fact]
    public void Scan_EmptyInputListAndNoInputs_FailsWithUsage()
    {
        string root = Path.Combine(Path.GetTempPath(), "m2p-scan-inputlist-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            string listPath = Path.Combine(root, "inputs.txt");
            File.WriteAllText(listPath, "\n");

            (int exit, _, string stderr) = RunScan("scan", "--input-list", listPath);

            Assert.Equal(1, exit);
            Assert.Contains("Usage:", stderr);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

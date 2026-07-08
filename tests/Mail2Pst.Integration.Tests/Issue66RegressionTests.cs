// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mail2Pst.Cli;
using Mail2Pst.TestSupport;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// PERMANENT regression for GitHub issue #66 (v0.3.2): scanning a profile with hundreds of
/// mail folders must ride the --input-list file transport, because passing each folder as a
/// repeated --input argument exceeds Windows' 32,767-char CreateProcess command-line limit
/// at roughly 250+ folders. This test never leaves the suite.
/// </summary>
// Tier 1: integration smoke — every PR.
[Collection("ConsoleCapture")]
public class Issue66RegressionTests
{
    [Fact]
    public void Scan_Profile400Folders_SucceedsViaInputListTransport()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithDeepRoot(approximatePrefixChars: 100)
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolders(count: 400, namePattern: "Projects-Archive-Some-Fairly-Long-Folder-Name-{0:D3}", messagesEach: 2)
            .Build();

        // Keep the fixture honest: the OLD transport (one "--input <path>" pair per folder)
        // must exceed the Windows CreateProcess limit, or this test no longer reproduces #66.
        // Lower bound: real legacy argv also had "scan"/"--progress" tokens and quoting.
        long legacyArgvLowerBound = profile.MailFilePaths.Sum(p => (long)p.Length + "--input".Length + 2);
        Assert.True(legacyArgvLowerBound > 32_767,
            $"Fixture too small to reproduce #66: would-be argv is only {legacyArgvLowerBound} chars.");

        // #66 is argv overflow ONLY — individual paths stay below classic MAX_PATH so this
        // regression never entangles with Windows long-path behavior (that's contract C4, 1B).
        Assert.All(profile.MailFilePaths, p => Assert.True(p.Length < 260,
            $"Path exceeds classic MAX_PATH and would conflate #66 with C4: {p.Length} chars"));

        string listPath = Path.Combine(Path.GetTempPath(), "m2p-issue66-list-" + Guid.NewGuid() + ".txt");
        File.WriteAllLines(listPath, profile.MailFilePaths);
        try
        {
            var sw = new StringWriter();
            TextWriter original = Console.Out;
            Console.SetOut(sw);
            int exit;
            try
            {
                exit = ScanCommand.Run(new[] { "--input-list", listPath });
            }
            finally
            {
                Console.SetOut(original);
            }

            Assert.Equal(0, exit);
            using JsonDocument doc = JsonDocument.Parse(sw.ToString());
            JsonElement totals = doc.RootElement.GetProperty("totals");
            // "sources" = one scan-result entry per input mbox file — exactly the 400
            // generated folder files, nothing else.
            Assert.Equal(400, totals.GetProperty("sources").GetInt32());
            Assert.Equal(800, totals.GetProperty("messages").GetInt32());
        }
        finally
        {
            File.Delete(listPath);
        }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mail2Pst.Cli;                    // ExportCommand (internal, visible via InternalsVisibleTo)
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Parsing;           // MboxParser / ParseResult
using Mail2Pst.TestSupport;
using Xunit;

namespace Mail2Pst.Integration.Tests.Reverse;

// Console.SetOut is process-global; in-process ExportCommand tests capture it, so they must not run in
// parallel with each other or with any other console-capturing test.
[CollectionDefinition("Console output", DisableParallelization = true)]
public sealed class ConsoleOutputCollection { }

// In-process ExportCommand.Run tests for scenarios that need a specifically-shaped PST. Builds the PST
// with the forward pipeline (RoundTripHarness.Convert) and captures the CLI's stdout via Console.SetOut.
[Collection("Console output")]
public class ExportCommandTests
{
    private static (int exit, JsonElement[] events) RunExport(params string[] args)
    {
        var sw = new StringWriter();
        TextWriter original = Console.Out;
        int exit;
        Console.SetOut(sw);
        try { exit = ExportCommand.Run(args); }
        finally { Console.SetOut(original); }

        JsonElement[] events = sw.ToString()
            .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
            .Select(l => { using JsonDocument d = JsonDocument.Parse(l); return d.RootElement.Clone(); })
            .ToArray();
        return (exit, events);
    }

    // Build a PST whose single mail folder is named EXACTLY `folderName` (TargetFolderPath is honored
    // verbatim; mirror mode would strip the extension, so a raw source-file name cannot produce this).
    private static (string pst, string convertDir) BuildPstWithTargetFolder(string folderName)
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Src", 1).Build();
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = true,
            Sources = { new SourceConfig { Type = "mbox", Path = profile.Folders[0].FilePath, TargetFolderPath = new List<string> { folderName } } },
        }}};
        string dir = Path.Combine(Path.GetTempPath(), "m2p-exportcmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (outputs, _) = RoundTripHarness.Convert(config, dir);
        return (Assert.Single(outputs), dir);
    }

    private static (string pst, string convertDir) BuildMirrorPst(GeneratedProfile profile)
    {
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = true,
            Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
        }}};
        string dir = Path.Combine(Path.GetTempPath(), "m2p-exportcmd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (outputs, _) = RoundTripHarness.Convert(config, dir);
        return (Assert.Single(outputs), dir);
    }

    private static string FreshOutDir() =>
        Path.Combine(Path.GetTempPath(), "m2p-exportcmd-out-" + Guid.NewGuid().ToString("N"));

    private static void CleanupSiblings(string outRoot)
    {
        foreach (string s in new[] { outRoot + ".export-report.json", outRoot + ".export-report.txt" })
            if (File.Exists(s)) File.Delete(s);
    }

    [Theory]
    [InlineData("export-report.json")]
    [InlineData("export-report.txt")]
    public void Export_FolderNamedLikeReport_DoesNotOverwriteMailbox_ReportGoesToSibling(string reservedName)
    {
        var (pst, convertDir) = BuildPstWithTargetFolder(reservedName);
        string outRoot = FreshOutDir();
        try
        {
            (int exit, JsonElement[] events) = RunExport("--input", pst, "--output", outRoot);
            Assert.Equal(0, exit);

            string outRootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outRoot));
            string mailbox = Path.Combine(outRoot, reservedName);                 // the MAILBOX, INSIDE the tree
            string reportJson = outRootFull + ".export-report.json";              // reports are SIBLINGS
            string reportTxt = outRootFull + ".export-report.txt";

            Assert.True(File.Exists(mailbox), "the mailbox named like a report must survive");
            ParseResult parsed = Assert.Single(new MboxParser().Parse(mailbox));
            Assert.True(parsed.Success);
            Assert.NotNull(parsed.Message);                                       // its message payload is intact

            Assert.True(File.Exists(reportJson), "the report must be written OUTSIDE the mbox tree");
            Assert.True(File.Exists(reportTxt));
            Assert.NotEqual(Path.GetFullPath(mailbox), reportJson);
            Assert.NotEqual(Path.GetFullPath(mailbox), reportTxt);

            using JsonDocument r = JsonDocument.Parse(File.ReadAllText(reportJson));
            Assert.True(r.RootElement.GetProperty("messagesExported").GetInt32() >= 1);
            Assert.Contains(events, e => e.GetProperty("type").GetString() == "done");
        }
        finally
        {
            Directory.Delete(convertDir, true);
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
            CleanupSiblings(outRoot);
        }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Reverse;
using Mail2Pst.Core.Writing;
using Mail2Pst.TestSupport;
using MimeKit;
using Xunit;

namespace Mail2Pst.Integration.Tests.Reverse;

public class PstExportRunnerTests
{
    private static (IReadOnlyList<string> outputs, string dir) ConvertProfile(ConversionConfig config)
    {
        string dir = Path.Combine(Path.GetTempPath(), "m2p-rev-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (outputs, _) = RoundTripHarness.Convert(config, dir);
        return (outputs, dir);
    }

    private static string FreshOutDir() =>
        Path.Combine(Path.GetTempPath(), "m2p-rev-out-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Run_Success_PublishesOnlyFinalOutputRoot()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("Inbox", messageCount: 2)
            .WithFolder("Sent", messageCount: 1)
            .Build();
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = false,
            Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
        }}};
        var (outputs, convertDir) = ConvertProfile(config);
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        string parent = Path.GetDirectoryName(outRoot)!;
        try
        {
            ExportReport report = new PstExportRunner().Run(pst, outRoot, includeEmpty: false);

            Assert.Equal(3, report.MessagesExported);
            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(outRoot)), report.OutputRoot);
            Assert.Equal(0, report.SkippedCount);
            Assert.True(Directory.Exists(outRoot));
            Assert.True(File.Exists(Path.Combine(outRoot, "Inbox")));
            // Report mbox paths resolve UNDER the published root (not staging, no traversal).
            Assert.All(report.MboxFiles, f =>
            {
                string rel = Path.GetRelativePath(Path.GetFullPath(outRoot), Path.GetFullPath(f));
                Assert.False(rel.StartsWith("..", StringComparison.Ordinal));
                Assert.False(Path.IsPathRooted(rel));
            });
            // No staging dir survives publication.
            Assert.Empty(Directory.GetDirectories(parent, Path.GetFileName(outRoot) + ".partial-*"));
        }
        finally
        {
            Directory.Delete(convertDir, true);
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        }
    }
}

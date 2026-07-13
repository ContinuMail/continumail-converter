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
using Mail2Pst.Core.Reverse;
using Mail2Pst.TestSupport;
using Xunit;

namespace Mail2Pst.Integration.Tests.Reverse;

public class ExportRoundTripTests
{
    // Headline gate: a NESTED profile -> forward ConversionRunner -> PST -> reverse export -> mbox tree,
    // re-parsed with our own MboxParser. Asserts nested folder structure via RELATIVE output paths (leaf
    // names would lose hierarchy and collide), per-folder message counts, and per-message fidelity.
    [Fact]
    public void ForwardThenReverse_PreservesNestedStructureCountsAndFields()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("InboxSrc", messageCount: 3)
            .WithFolder("SentSrc", messageCount: 2)
            .WithFolder("EmptySrc", messageCount: 0)
            .Build();

        // Map the three sources to a NESTED destination via explicit TargetFolderPath:
        //   Parent/Inbox (3), Sent (2), Empty (0).
        GeneratedFolder inbox = profile.Folders.First(f => f.Name == "InboxSrc");
        GeneratedFolder sent = profile.Folders.First(f => f.Name == "SentSrc");
        GeneratedFolder empty = profile.Folders.First(f => f.Name == "EmptySrc");
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = true,
            Sources =
            {
                new SourceConfig { Type = "mbox", Path = inbox.FilePath, TargetFolderPath = new List<string> { "Parent", "Inbox" } },
                new SourceConfig { Type = "mbox", Path = sent.FilePath,  TargetFolderPath = new List<string> { "Sent" } },
                new SourceConfig { Type = "mbox", Path = empty.FilePath, TargetFolderPath = new List<string> { "Empty" } },
            },
        }}};

        string convertDir = Path.Combine(Path.GetTempPath(), "m2p-rt-conv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(convertDir);
        string outRoot = Path.Combine(Path.GetTempPath(), "m2p-rt-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (outputs, _) = RoundTripHarness.Convert(config, convertDir);
            string pst = Assert.Single(outputs);

            ExportReport report = new PstExportRunner().Run(pst, outRoot, includeEmpty: true);
            Assert.Equal(5, report.MessagesExported);
            Assert.Equal(0, report.SkippedCount);

            var countByPath = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string mbox in report.MboxFiles)
            {
                int n = 0;
                foreach (ParseResult r in new MboxParser().Parse(mbox))
                {
                    Assert.True(r.Success);
                    MailMessage m = r.Message!;
                    Assert.StartsWith("Generated message", m.Subject);
                    Assert.Equal("sender@example.com", m.From!.Email);
                    Assert.Contains(m.To, a => a.Email == "alice@example.com");
                    Assert.Contains("Synthetic body", m.TextBody ?? m.HtmlBody ?? string.Empty);
                    n++;
                }
                string relative = Path.GetRelativePath(outRoot, mbox).Replace(Path.DirectorySeparatorChar, '/');
                countByPath[relative] = n;
            }

            // Nested structure via relative paths (not leaf names).
            Assert.Equal(3, countByPath["Parent.sbd/Inbox"]);
            Assert.Equal(2, countByPath["Sent"]);
            Assert.Equal(0, countByPath["Parent"]);   // structural parent: empty mbox + Parent.sbd/
            Assert.Equal(0, countByPath["Empty"]);    // empty leaf, emitted because includeEmpty=true

            // No warnings expected on this clean fixture. If a benign one ever appears, document + filter it
            // here rather than loosening the assertion.
            Assert.Empty(report.Warnings);
        }
        finally
        {
            Directory.Delete(convertDir, true);
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        }
    }
}

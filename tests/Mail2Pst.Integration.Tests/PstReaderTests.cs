// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class PstReaderTests
{
    private static (IReadOnlyList<string> outputs, string outDir) ConvertSample()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-reader-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                    Sources = new List<SourceConfig> { new() { Type = "mbox", Path = Path.Combine(fixtures, "sample.mbox") } },
                },
            },
        };
        var report = new ConversionRunner().Run(config, outDir);
        return (report.OutputFiles, outDir);
    }

    [Fact]
    public void Read_SampleMbox_ReturnsFolderWithCoreFields()
    {
        var (outputs, outDir) = ConvertSample();
        try
        {
            IReadOnlyList<ReadFolder> folders = PstReader.Read(outputs);

            ReadFolder sample = Assert.Single(folders, f => f.Path.SequenceEqual(new[] { "sample" }));
            Assert.Equal(2, sample.Messages.Count);

            // NOTE: the parser angle-brackets Message-IDs (MimeMessageMapper.EnsureAngleBrackets),
            // so the stored value is "<msg1@example.com>". If any anchor below fails, open
            // fixtures/sample.mbox and correct the literal to the ACTUAL stored value — never weaken
            // the assertion to a generic non-empty check.
            ReadBackMessage m1 = Assert.Single(sample.Messages, m => m.MessageId == "<msg1@example.com>");
            Assert.Equal("Hello from Alice", m1.Subject);
            Assert.Equal("alice@example.com", m1.FromAddress);
            Assert.True(m1.HasNonEmptyBody);
            Assert.NotNull(m1.Date);
            Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                         m1.Date!.Value.ToUnixTimeSeconds());
        }
        finally { Directory.Delete(outDir, true); }
    }

    [Fact]
    public void Read_SampleMbox_PopulatesRecipientsWithKind()
    {
        var (outputs, outDir) = ConvertSample();
        try
        {
            ReadFolder sample = PstReader.Read(outputs).Single(f => f.Path.SequenceEqual(new[] { "sample" }));
            // Bracketed Message-ID (see note in Read_SampleMbox_ReturnsFolderWithCoreFields).
            ReadBackMessage m2 = sample.Messages.Single(m => m.MessageId == "<msg2@example.com>");

            Assert.Contains(m2.Recipients, r => r.Address == "bob@example.com" && r.Kind == RecipientKind.To);
            Assert.Contains(m2.Recipients, r => r.Address == "dave@example.com" && r.Kind == RecipientKind.To);
            Assert.Contains(m2.Recipients, r => r.Address == "eve@example.com" && r.Kind == RecipientKind.Cc);
        }
        finally { Directory.Delete(outDir, true); }
    }

    [Fact]
    public void Read_AttachmentsMbox_ListsVisibleAndExcludesInline()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-reader-att-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                        Sources = new List<SourceConfig> { new() { Type = "mbox", Path = Path.Combine(fixtures, "mbox-with-attachments.mbox") } },
                    },
                },
            };
            var report = new ConversionRunner().Run(config, outDir);

            var allNames = PstReader.Read(report.OutputFiles)
                .SelectMany(f => f.Messages).SelectMany(m => m.AttachmentNames).ToList();

            // Anchors below are real fixture values; if one fails, open
            // fixtures/mbox-with-attachments.mbox and fix to the actual value — do not weaken the test.
            // The forwarded message surfaces as attached-message.eml; the inline CID image is hidden.
            Assert.Contains("attached-message.eml", allNames);
            Assert.DoesNotContain(allNames, n => n.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
            // A known real attachment filename from the fixture:
            Assert.Contains("hello.txt", allNames);
        }
        finally { Directory.Delete(outDir, true); }
    }
}

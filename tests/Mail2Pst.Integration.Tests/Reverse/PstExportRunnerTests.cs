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

    // A reconstructor that throws on the Nth message — forces an UNEXPECTED mid-export failure (fatal path).
    private sealed class ThrowOnNthReconstructor : IMimeReconstructor
    {
        private readonly int _throwOn;
        private int _seen;
        public ThrowOnNthReconstructor(int throwOn) => _throwOn = throwOn;
        public MimeMessage Reconstruct(PstMailMessage message)
        {
            if (++_seen == _throwOn) throw new InvalidOperationException("boom during reconstruction");
            var m = new MimeMessage();
            m.From.Add(new MailboxAddress(string.Empty, message.FromAddress ?? "s@example.com"));
            m.To.Add(new MailboxAddress(string.Empty, "r@example.com"));
            m.Subject = message.Subject ?? string.Empty;
            m.Date = message.Date ?? DateTimeOffset.UnixEpoch;
            m.Body = new TextPart("plain") { Text = message.PlainBody ?? string.Empty, ContentTransferEncoding = ContentEncoding.SevenBit };
            return m;
        }
    }

    // Emits a known warning through its injected sink, then a minimal real reconstruction.
    private sealed class WarningReconstructor : IMimeReconstructor
    {
        private readonly Action<string>? _onWarning;
        public WarningReconstructor(Action<string>? onWarning) => _onWarning = onWarning;
        public MimeMessage Reconstruct(PstMailMessage message)
        {
            _onWarning?.Invoke("reconstructor-warning");
            var m = new MimeMessage();
            m.From.Add(new MailboxAddress(string.Empty, message.FromAddress ?? "s@example.com"));
            m.To.Add(new MailboxAddress(string.Empty, "r@example.com"));
            m.Subject = message.Subject ?? string.Empty;
            m.Date = message.Date ?? DateTimeOffset.UnixEpoch;
            m.Body = new TextPart("plain") { Text = message.PlainBody ?? string.Empty, ContentTransferEncoding = ContentEncoding.SevenBit };
            return m;
        }
    }

    private static ConversionConfig OneInbox(GeneratedProfile profile, int _) => new()
    {
        Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = false,
            Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
        }}
    };

    [Fact]
    public void Run_NonEmptyOutputRoot_IsRejectedWithoutModification()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 1).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        Directory.CreateDirectory(outRoot);
        string sentinel = Path.Combine(outRoot, "existing.txt");
        File.WriteAllText(sentinel, "keep me");
        try
        {
            Assert.ThrowsAny<Exception>(() => new PstExportRunner().Run(pst, outRoot, includeEmpty: false));
            Assert.True(File.Exists(sentinel));                           // destination untouched
            Assert.Equal("keep me", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(convertDir, true); Directory.Delete(outRoot, true); }
    }

    [Fact]
    public void Run_OutputPathIsExistingFile_FailsBeforeCreatingStaging()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 1).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outFile = Path.Combine(Path.GetTempPath(), "m2p-rev-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(outFile, "i am a file");
        string parent = Path.GetDirectoryName(outFile)!;
        try
        {
            Assert.Throws<IOException>(() => new PstExportRunner().Run(pst, outFile, includeEmpty: false));
            Assert.Empty(Directory.GetDirectories(parent, Path.GetFileName(outFile) + ".partial-*"));   // no staging created
            Assert.Equal("i am a file", File.ReadAllText(outFile));                                      // file untouched
        }
        finally { Directory.Delete(convertDir, true); File.Delete(outFile); }
    }

    [Fact]
    public void Run_OutputRootWithTrailingSeparator_PublishesCorrectly()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 2).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        string withSep = outRoot + Path.DirectorySeparatorChar;   // trailing separator
        try
        {
            ExportReport report = new PstExportRunner().Run(pst, withSep, includeEmpty: false);
            Assert.Equal(2, report.MessagesExported);
            Assert.Equal(outRoot, report.OutputRoot);                 // normalized (separator trimmed)
            Assert.True(File.Exists(Path.Combine(outRoot, "Inbox")));
            Assert.Empty(Directory.GetDirectories(outRoot, "*.partial-*"));   // staging NOT nested inside dest
        }
        finally { Directory.Delete(convertDir, true); if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true); }
    }

    [Fact]
    public void Run_FatalMidExport_RemovesStagingOutput()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 2).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        string parent = Path.GetDirectoryName(outRoot)!;
        try
        {
            var runner = new PstExportRunner(_ => new ThrowOnNthReconstructor(throwOn: 2));
            Assert.Throws<InvalidOperationException>(() => runner.Run(pst, outRoot, includeEmpty: false));

            Assert.False(Directory.Exists(outRoot));                                              // never published
            Assert.Empty(Directory.GetDirectories(parent, Path.GetFileName(outRoot) + ".partial-*")); // staging cleaned up
        }
        finally { Directory.Delete(convertDir, true); if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true); }
    }

    [Fact]
    public void Run_InvalidPst_PropagatesAndCreatesNoOutput()
    {
        string bogusPst = Path.Combine(Path.GetTempPath(), "m2p-not-a-pst-" + Guid.NewGuid().ToString("N") + ".pst");
        File.WriteAllText(bogusPst, "this is not a PST file");
        string outRoot = FreshOutDir();
        string parent = Path.GetDirectoryName(outRoot)!;
        try
        {
            Assert.ThrowsAny<Exception>(() => new PstExportRunner().Run(bogusPst, outRoot, includeEmpty: false));
            Assert.False(Directory.Exists(outRoot));                                              // no output published
            Assert.Empty(Directory.GetDirectories(parent, Path.GetFileName(outRoot) + ".partial-*")); // no staging created
        }
        finally { File.Delete(bogusPst); if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true); }
    }

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

    [Fact]
    public void Run_ReconstructorWarning_ReachesReportAndLiveSink()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 1).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        try
        {
            var live = new List<string>();
            var runner = new PstExportRunner(onWarning => new WarningReconstructor(onWarning));
            ExportReport report = runner.Run(pst, outRoot, includeEmpty: false, onWarning: live.Add);

            Assert.Contains("reconstructor-warning", report.Warnings);   // shared collector -> report
            Assert.Contains("reconstructor-warning", live);              // ... and the live sink
        }
        finally { Directory.Delete(convertDir, true); if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true); }
    }

    [Fact]
    public void Run_Progress_TicksOncePerSuccessfullyWrittenMessage()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 3).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        try
        {
            var ticks = new List<ExportProgress>();
            ExportReport report = new PstExportRunner().Run(pst, outRoot, includeEmpty: false, onProgress: ticks.Add);

            Assert.Equal(3, ticks.Count);
            Assert.Equal(new[] { 1, 2, 3 }, ticks.Select(t => t.MessagesExported).ToArray());
            Assert.All(ticks, t => Assert.False(string.IsNullOrEmpty(t.CurrentFolder)));
            Assert.Equal(report.MessagesExported, ticks[^1].MessagesExported);
        }
        finally { Directory.Delete(convertDir, true); if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true); }
    }

    [Fact]
    public void Run_ProgressCountsOnlySuccessfullyWrittenMessages()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com").WithFolder("Inbox", 3).Build();
        var (outputs, convertDir) = ConvertProfile(OneInbox(profile, 0));
        string pst = Assert.Single(outputs);

        string outRoot = FreshOutDir();
        try
        {
            var ticks = new List<ExportProgress>();
            var runner = new PstExportRunner(_ => new ThrowOnNthReconstructor(throwOn: 2));   // msg 2 throws
            Assert.Throws<InvalidOperationException>(
                () => runner.Run(pst, outRoot, includeEmpty: false, onProgress: ticks.Add));

            // Only message 1 was written before the fatal throw, so progress saw exactly one tick.
            Assert.Equal(new[] { 1 }, ticks.Select(t => t.MessagesExported).ToArray());
        }
        finally { Directory.Delete(convertDir, true); if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true); }
    }

    // Attachment OpenRead closures are only valid while the message is current in the PST enumeration. If the
    // runner read attachments after the PST closed (e.g. by materializing the stream first), the payload would
    // be empty. Proving a visible attachment's bytes survive into the exported mbox proves the PST-bound
    // lifetime is respected end-to-end (reconstruction + serialization + re-parse).
    [Fact]
    public void Run_PreservesAttachmentPayloadThroughFullExport()
    {
        byte[] payload = { 10, 20, 30, 40, 50, 60, 70, 80 };
        var msg = new MailMessage
        {
            MessageId = "<attach-payload@test>",
            Subject = "Attachment payload",
            Attachments = new List<MailAttachment>
            {
                new() { FileName = "note.bin", MimeType = "application/octet-stream", Content = AttachmentContent.FromBytes(payload) },
            },
        };

        string convertDir = Path.Combine(Path.GetTempPath(), "m2p-rev-att-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(convertDir);
        string outRoot = FreshOutDir();
        try
        {
            var plan = new PstOutputPlan { Name = "Att", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = false };
            PlannedMessage[] planned = [ new() { Message = msg, TargetFolderPath = new[] { "Inbox" } } ];
            string pst = Assert.Single(new PstWriter().WritePlan(plan, planned, convertDir, new ConversionReport()));

            ExportReport report = new PstExportRunner().Run(pst, outRoot, includeEmpty: false);
            Assert.Equal(1, report.MessagesExported);

            string inbox = Path.Combine(outRoot, "Inbox");
            Assert.True(File.Exists(inbox));
            ParseResult parsed = Assert.Single(new MboxParser().Parse(inbox));
            Assert.True(parsed.Success);
            MailAttachment att = Assert.Single(parsed.Message!.Attachments);
            using (Stream content = att.Content.OpenRead())
            using (var ms = new MemoryStream())
            {
                content.CopyTo(ms);
                Assert.Equal(payload, ms.ToArray());
            }
            att.Content.Dispose();
        }
        finally
        {
            Directory.Delete(convertDir, true);
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, true);
        }
    }
}

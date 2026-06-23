// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterErrorTaxonomyTests
{
    // The write-side recoverable allowlist is intentionally EMPTY for v1: a PST writer
    // failure is fatal unless proven to be one bad message. This pins that decision.
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(System.Text.EncoderFallbackException))]
    public void IsRecoverableWriteError_IsFalse_ForAllRepresentativeExceptions(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.False(PstWriter.IsRecoverableWriteError(ex));
    }

    private static PlannedMessage SmallMessage(int i) => new()
    {
        TargetFolderPath = new[] { "Imported Mail" },
        Message = new MailMessage
        {
            Subject = $"Message {i}",
            TextBody = "body",
            Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = $"#{i}" },
            Attachments = new List<MailAttachment>(),
        },
    };

    // Overrides the per-message write seam to throw a chosen exception when `shouldThrow`
    // returns true (called once per message with the 0-based write index).
    private sealed class ThrowingPstWriter : PstWriter
    {
        private readonly Func<int, bool> _shouldThrow;
        private readonly Exception _toThrow;
        private int _calls;

        public ThrowingPstWriter(Func<int, bool> shouldThrow, Exception toThrow, int checkIntervalMessages = 500)
            : base(checkIntervalMessages)
        {
            _shouldThrow = shouldThrow;
            _toThrow = toThrow;
        }

        internal override void WriteMessageCore(PSTFile file, PSTFolder folder, MailMessage message)
        {
            if (_shouldThrow(_calls++)) throw _toThrow;
            base.WriteMessageCore(file, folder, message);
        }
    }

    [Fact]
    public void WritePlan_FatalWriteError_DeletesInProgressPartAndRethrows()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Personal", MaxSizeBytes = 100L * 1024 * 1024 };
            var messages = Enumerable.Range(0, 5).Select(SmallMessage).ToList();
            var report = new ConversionReport();
            var writer = new ThrowingPstWriter(i => i == 2, new InvalidOperationException("boom"));

            Assert.Throws<InvalidOperationException>(() =>
                writer.WritePlan(plan, messages, outputDir, report));

            // No split happened; the single in-progress part must be deleted.
            Assert.Empty(Directory.GetFiles(outputDir, "*.pst"));
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_FatalAfterSplit_KeepsCompletedPartDeletesCurrent()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 20_000 };
            string bigBody = new string('x', 5000);
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 20; i++)
            {
                var m = SmallMessage(i);
                m.Message.TextBody = bigBody;
                messages.Add(m);
            }
            var report = new ConversionReport();
            string part2 = Path.Combine(outputDir, "Archive-2.pst");

            // Throw on the first WriteMessageCore call once the split has created part 2.
            // This also exercises the "new part created, no message yet written to it" edge
            // case: the fatal fires BEFORE base.WriteMessageCore, so part 2 exists but is
            // empty (BeginSavingChanges only) when the abort happens.
            var writer = new ThrowingPstWriter(_ => File.Exists(part2),
                new InvalidOperationException("boom"), checkIntervalMessages: 2);

            Assert.Throws<InvalidOperationException>(() =>
                writer.WritePlan(plan, messages, outputDir, report));

            string part1 = Path.Combine(outputDir, "Archive-1.pst");
            Assert.True(File.Exists(part1), "completed part 1 must remain on disk");
            Assert.False(File.Exists(part2), "in-progress part 2 must be deleted");
            // Belt-and-suspenders against split-naming assumptions: the ONLY surviving .pst
            // is the completed part 1.
            var pstFiles = Directory.GetFiles(outputDir, "*.pst").Select(Path.GetFileName).ToList();
            Assert.Contains("Archive-1.pst", pstFiles);
            Assert.DoesNotContain("Archive-2.pst", pstFiles);
            Assert.DoesNotContain("Archive.pst", pstFiles);
        }
        finally { Directory.Delete(outputDir, true); }
    }

    [Fact]
    public void WritePlan_SplitCreationFails_KeepsCompletedPartsAndLeavesNoOrphan()
    {
        long templateSize = PSTFile.EmptyStoreSizeBytes;
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        try
        {
            // Plant a DIRECTORY where the THIRD part (Archive-3.pst) would be written, so the
            // SECOND split fails while creating it — AFTER part 2 is already complete on disk.
            // A failed split must surface as fatal but must NOT delete a completed part, and
            // must not leave a stray part behind. (Regression: the pre-fix code left _currentPath
            // pointing at the completed part 2, so the fatal cleanup deleted it.)
            Directory.CreateDirectory(Path.Combine(outputDir, "Archive-3.pst"));

            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = templateSize + 20_000 };
            string bigBody = new string('x', 5000);
            var messages = new List<PlannedMessage>();
            for (int i = 0; i < 20; i++)
            {
                PlannedMessage m = SmallMessage(i);
                m.Message.TextBody = bigBody;
                messages.Add(m);
            }

            var writer = new PstWriter();
            Assert.ThrowsAny<Exception>(() => writer.WritePlan(plan, messages, outputDir, new ConversionReport()));

            string part1 = Path.Combine(outputDir, "Archive-1.pst");
            string part2 = Path.Combine(outputDir, "Archive-2.pst");
            Assert.True(File.Exists(part1), "completed part 1 must remain on disk");
            Assert.True(File.Exists(part2), "a failed split must NOT delete the completed part 2");

            // The only surviving part FILES are the two completed parts — no stray/orphan part.
            var pstFiles = Directory.GetFiles(outputDir, "*.pst").Select(Path.GetFileName).ToList();
            Assert.Equal(2, pstFiles.Count);
            Assert.Contains("Archive-1.pst", pstFiles);
            Assert.Contains("Archive-2.pst", pstFiles);
        }
        finally { Directory.Delete(outputDir, true); }
    }
}

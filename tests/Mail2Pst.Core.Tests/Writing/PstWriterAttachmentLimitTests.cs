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
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstWriterAttachmentLimitTests
{

    // PidTagAttachSize is a PT_LONG (signed 32-bit), so content up to int.MaxValue bytes
    // is representable and anything above it is not (and would also force a >2 GB single
    // allocation in ReadAllBytes).
    [Theory]
    [InlineData(0L, false)]
    [InlineData((long)int.MaxValue, false)]
    [InlineData((long)int.MaxValue + 1, true)]
    public void AttachmentTooLarge_FlagsOnlyContentAboveInt32Max(long length, bool expected)
    {
        Assert.Equal(expected, PstWriter.AttachmentTooLarge(length));
    }

    [Fact]
    public void IsRecoverableWriteError_True_ForAttachmentTooLarge()
    {
        var ex = new AttachmentTooLargeException("big.bin", (long)int.MaxValue + 1);
        Assert.True(PstWriter.IsRecoverableWriteError(ex));
    }

    [Fact]
    public void WritePlan_AttachmentExceedsPstLimit_SkipsThatMessageAndConvertsRest()
    {
        string outputDir = Path.Combine(Path.GetTempPath(), "mail2pst-attlimit-" + Guid.NewGuid());
        Directory.CreateDirectory(outputDir);
        string tiny = Path.GetTempFileName();
        File.WriteAllBytes(tiny, new byte[] { 1, 2, 3 });
        try
        {
            // Declare a >2 GB attachment WITHOUT allocating it: FromTempFile takes an explicit
            // length. The message must be skipped (recorded), not abort the run or wrap the cast.
            var oversized = new PlannedMessage
            {
                TargetFolderPath = new[] { "Imported Mail" },
                Message = new MailMessage
                {
                    Subject = "huge",
                    Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = "#big" },
                    Attachments = new List<MailAttachment>
                    {
                        new()
                        {
                            FileName = "big.bin",
                            MimeType = "application/octet-stream",
                            Content = AttachmentContent.FromTempFile(tiny, (long)int.MaxValue + 1),
                        },
                    },
                },
            };
            var normal = new PlannedMessage
            {
                TargetFolderPath = new[] { "Imported Mail" },
                Message = new MailMessage
                {
                    Subject = "ok",
                    TextBody = "body",
                    Source = new SourceReference { SourcePath = "Archive.mbox", Identifier = "#ok" },
                    Attachments = new List<MailAttachment>(),
                },
            };

            var plan = new PstOutputPlan { Name = "Archive", MaxSizeBytes = 100L * 1024 * 1024 };
            var report = new ConversionReport();
            var writer = new PstWriter();
            List<string> outputs = writer.WritePlan(plan, new[] { oversized, normal }, outputDir, report);

            Assert.Equal(1, report.ConvertedCount);   // the normal message converted
            Assert.Equal(1, report.SkippedCount);      // the oversized message skipped, run not fatal
            Assert.Single(outputs);                    // single completed part, no split or abort
        }
        finally
        {
            if (File.Exists(tiny)) File.Delete(tiny);
            Directory.Delete(outputDir, true);
        }
    }
}

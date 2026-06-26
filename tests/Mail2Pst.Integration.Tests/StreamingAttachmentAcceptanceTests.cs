// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class StreamingAttachmentAcceptanceTests
{
    private static readonly TimeSpan ValidatorTimeout = TimeSpan.FromSeconds(60);

    // [L5] residual: pst-validate reads structural metadata (opened, folder paths, message counts)
    // but NOT PidTagAttachData — it cannot independently verify attachment payload bytes. The
    // closed-loop byte-equality check (Task 5, StreamingAttachmentRoundTripTests) is the only
    // payload verification; both the round-trip test and this structural gate use the same vendored
    // DataTree/block machinery, so a write/read-symmetric encoding bug could pass both. Accepted
    // residual, justified: the on-disk *encoding* is the same shared DataTree/block machinery as the
    // existing byte[] large-attachment path; only the build/eviction *sequencing* is new in
    // Target-A. Extending tools/pst-validate to byte-compare PidTagAttachData via the MIT
    // outlook-pst crate is out of proportion for this Low finding. Formal sign-off recorded in
    // Task 7's acceptance memo.
    [SkippableFact]
    public void StreamedLargeAttachment_PstValidatesClean_WithIndependentReader()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe to run the independent-reader gate.");

        // 9 MB > 8,347,696 B XXBlock boundary — exercises the full XXBlock streaming machinery.
        // StreamingThresholdBytes is lowered to 1 B so streaming triggers without needing a 16+ MB
        // fixture (avoids slow test; the threshold is internal and accessible via InternalsVisibleTo).
        const int attachmentSize = 9_000_000;
        byte[] payload = new byte[attachmentSize];
        for (int i = 0; i < attachmentSize; i++) payload[i] = (byte)((i * 31 + 7) & 0xFF);

        using var content = AttachmentContent.FromBytes(payload);
        var msg = new MailMessage
        {
            MessageId = "<stream-accept-gate@test>",
            Subject = "Streaming acceptance gate — XXBlock attachment",
            Attachments = new List<MailAttachment>
            {
                new()
                {
                    FileName = "large.bin",
                    MimeType = "application/octet-stream",
                    Content = content,
                },
            },
        };

        string outDir = Path.Combine(Path.GetTempPath(), "m2p-stream-accept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var plan = new PstOutputPlan
            {
                Name = "StreamAccept",
                MaxSizeBytes = 100L * 1024 * 1024,
                IncludeEmptyFolders = false,
            };
            PlannedMessage[] planned =
            [
                new() { Message = msg, TargetFolderPath = new[] { "Inbox" } },
            ];
            var report = new ConversionReport();
            var writer = new PstWriter();
            writer.StreamingThresholdBytes = 1;   // force streaming path for this <16 MB fixture

            List<string> outputs = writer.WritePlan(plan, planned, outDir, report);
            Assert.NotEmpty(outputs);

            // --- independent structural validation ---
            ValidatorResult r = PstValidatorRunner.Run(outputs[0], ValidatorTimeout);
            Assert.True(r.Opened,
                $"pst-validate could not open output PST: " +
                string.Join("; ", r.Errors.Select(e => $"{e.Stage}:{e.Message}")));
            Assert.Empty(r.Errors);

            // The single message must appear somewhere in the store.
            long total = r.Folders.Sum(f => f.MessageCount);
            Assert.Equal(1L, total);

            // [L5] residual (see class-level comment): PidTagAttachData payload bytes are NOT
            // independently verified here — pst-validate does not expose attachment body bytes.
            // Structural integrity (AMap, NDB open, folder/message counts) is confirmed above.
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }
}

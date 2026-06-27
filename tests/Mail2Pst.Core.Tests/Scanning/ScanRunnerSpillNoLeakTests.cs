// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Parsing.Mbox;
using Mail2Pst.Core.Scanning;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

// Runs serially against the whole suite: the no-leak assertion snapshots the shared
// temp dir for `mail2pst-raw-*` files, and several other tests spill there too —
// DisableParallelization guarantees no concurrent creator races the snapshot.
[CollectionDefinition("ScanNoLeakSerial", DisableParallelization = true)]
public class ScanNoLeakSerialCollection { }

// Memory-safety regression for the parallel byte-range scan (Plan 2): a scan that
// spills (large messages) must leave NO `mail2pst-raw-*` temp files behind — every
// per-message spill buffer is disposed (the `using` in ScanRange/Parse) — and the
// spill must not alter results. Codifies the Task-8 manual check as an automated guard.
[Collection("ScanNoLeakSerial")]
public class ScanRunnerSpillNoLeakTests
{
    // Injects a tiny raw-message spill threshold into the scan parser so ordinary
    // small messages spill, exercising the spill→OpenRead→Dispose→delete cycle
    // through the real parallel ScanRunner path without needing a multi-MB fixture.
    private sealed class TinySpillScanRunner : ScanRunner
    {
        private readonly long _threshold;
        public TinySpillScanRunner(ScanRunnerOptions options, long spillThreshold) : base(options)
            => _threshold = spillThreshold;
        internal override MboxParser ResolveScanParser(string sourceType)
            => new MboxParser(measureOnly: true, rawSpillThreshold: _threshold);
    }

    private static string WriteMbox(int count, int bodyBytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-scanleak-" + Guid.NewGuid() + ".mbox");
        string body = new string('x', bodyBytes);
        var sb = new StringBuilder(count * (bodyBytes + 200));
        for (int i = 0; i < count; i++)
        {
            sb.Append("From s").Append(i).Append("@b Thu Jan 01 00:00:00 2026\r\n");
            sb.Append("Subject: m").Append(i).Append("\r\n\r\n");
            sb.Append(body).Append("\r\n\r\n");
        }
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string[] RawTempFiles() =>
        Directory.GetFiles(Path.GetTempPath(), "mail2pst-raw-*");

    [Fact]
    public void ParallelScan_ForcedSpill_LeavesNoTempFiles_AndMatchesNoSpill()
    {
        const long spillThreshold = 256;     // << body size, so every message spills
        const int bodyBytes = 2000;

        // Sanity: confirm these sizes really do spill (else the no-leak check is vacuous).
        using (var buf = new SpillableMessageBuffer(spillThreshold))
        {
            buf.Write(new byte[bodyBytes]);
            Assert.True(buf.SpilledToDisk, "test sizing is wrong — the buffer did not spill");
        }

        string mbox = WriteMbox(count: 40, bodyBytes: bodyBytes);
        try
        {
            var before = new HashSet<string>(RawTempFiles());

            // Tiny TargetChunkBytes forces many ranges → the parallel path; tiny threshold forces spills.
            var spillOpts = new ScanRunnerOptions { TargetChunkBytes = 4096, MaxDegreeOfParallelism = 4 };
            ScanReport spilled = new TinySpillScanRunner(spillOpts, spillThreshold).Scan(new[] { mbox }, "mbox");

            // No spill temp file created by this scan survives.
            string[] leaked = RawTempFiles().Where(f => !before.Contains(f)).ToArray();
            Assert.True(leaked.Length == 0, "scan leaked spill temp files: " + string.Join(", ", leaked));

            // Spilling did not alter results: byte-identical to a sequential, no-spill scan.
            var baseOpts = new ScanRunnerOptions { TargetChunkBytes = long.MaxValue, MaxDegreeOfParallelism = 1 };
            ScanReport noSpill = new TinySpillScanRunner(baseOpts, long.MaxValue).Scan(new[] { mbox }, "mbox");

            Assert.Equal(40, spilled.Totals.Messages);
            Assert.Equal(noSpill.Totals.Messages, spilled.Totals.Messages);
            Assert.Equal(noSpill.Totals.Bytes, spilled.Totals.Bytes);
            Assert.Equal(noSpill.Totals.SourceBytes, spilled.Totals.SourceBytes);
            Assert.Equal(noSpill.Skipped.Count, spilled.Skipped.Count);
        }
        finally { File.Delete(mbox); }
    }
}

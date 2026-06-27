// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Scanning;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class ScanRunnerFatalTests
{
    // Minimal test seam: a parser whose ScanRange throws (per-path), injected via the internal
    // virtual ResolveScanParser hook on ScanRunner. Production resolution is untouched.
    private sealed class ThrowOnScanParser : MboxParser
    {
        private readonly Func<string, Exception> _ex;
        public ThrowOnScanParser(Func<string, Exception> ex) : base(measureOnly: true) => _ex = ex;

        public override RangeScanResult ScanRange(string path, long startOffset, long endOffset, Action<long>? onBytesRead)
            => throw _ex(path);
    }

    private sealed class SeamScanRunner : ScanRunner
    {
        private readonly MboxParser _parser;
        public SeamScanRunner(MboxParser parser) => _parser = parser;
        internal override MboxParser ResolveScanParser(string sourceType) => _parser;
    }

    private static string WriteTempMbox(string body)
    {
        string path = Path.Combine(Path.GetTempPath(), "m2p-fatal-" + Guid.NewGuid() + ".mbox");
        File.WriteAllText(path,
            "From a@b.com Mon Jan 01 00:00:00 2024\r\nSubject: s\r\n\r\n" + body + "\r\n");
        return path;
    }

    [Fact]
    public void UnexpectedException_IsFatal_LowestSourceIndexWins()
    {
        // Two sources both throw. The merge must surface the lowest-(sourceIndex, offset) one,
        // i.e. source 0's exception — not source 1's.
        string p0 = WriteTempMbox("zero");
        string p1 = WriteTempMbox("one");
        var parser = new ThrowOnScanParser(path =>
            new InvalidOperationException("boom for " + path));
        var runner = new SeamScanRunner(parser);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => runner.Scan(new[] { p0, p1 }, "mbox"));
            Assert.Contains(p0, ex.Message);        // source 0 wins
            Assert.DoesNotContain(p1, ex.Message);  // source 1 does NOT surface
        }
        finally { File.Delete(p0); File.Delete(p1); }
    }

    [Fact]
    public void SpillIoFailure_IsFatal_AndEmitsNoFinalProgress()
    {
        // A RawMessageSpillException (operational spill failure) is NOT a per-message skip — it must
        // surface as fatal, and because the rethrow happens BEFORE EmitFinal, no (total,total) lands.
        string p = WriteTempMbox("spill");
        var parser = new ThrowOnScanParser(_ =>
            new RawMessageSpillException("temp spill failed", new IOException("disk full")));
        var runner = new SeamScanRunner(parser);
        var progresses = new List<ScanProgress>();
        try
        {
            Assert.Throws<RawMessageSpillException>(
                () => runner.Scan(new[] { p }, "mbox", progresses.Add));
            // No success progress was emitted: in particular EmitFinal never fired (no 100% event).
            Assert.DoesNotContain(progresses, e => e.Bytes == e.TotalBytes);
        }
        finally { File.Delete(p); }
    }
}

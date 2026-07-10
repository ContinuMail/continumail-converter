// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Parsing;

namespace Mail2Pst.Fuzz;

/// <summary>
/// One target per fuzzed parser. Each runs the parser over the input bytes and swallows ONLY the
/// whitelisted graceful exceptions (MorkFormatException / FormatException / IOException) — the
/// documented "degrade to a skip" contract. Any other exception type escapes, so libFuzzer (or the
/// replay driver) records it as a crash: that is the bug class we are hunting.
/// </summary>
public static class FuzzTargets
{
    // The single whitelist: these three types are the documented "degrade to a skip" outcomes.
    // Anything else escaping the target is a finding. Both targets use the SAME filter.
    private static bool IsGraceful(Exception ex) =>
        ex is MorkFormatException or FormatException or IOException;

    public static void RunMork(ReadOnlySpan<byte> data)
    {
        // MorkReader is byte-friendly via Parse(Stream); no temp file needed.
        using var ms = new MemoryStream(data.ToArray());
        try
        {
            _ = MorkReader.Parse(ms);
        }
        catch (Exception ex) when (IsGraceful(ex)) { /* graceful: malformed/hostile Mork -> skip */ }
    }

    public static void RunMbox(ReadOnlySpan<byte> data)
    {
        // MboxParser.Parse is path-based, so spill the input to a scratch file per iteration.
        string path = Path.Combine(Path.GetTempPath(), $"m2p-fuzz-{Guid.NewGuid():N}.mbox");
        try
        {
            // The temp-file WRITE is inside the try on purpose: an IOException from a full/locked
            // temp dir is whitelisted, not a "crash". Draining the enumerable is what actually runs
            // the parse; per-message FormatException/IOException are already turned into
            // ParseResult.Failed inside the parser, so a well-behaved parser never throws here.
            File.WriteAllBytes(path, data.ToArray());
            _ = new MboxParser().Parse(path).ToList();
        }
        catch (Exception ex) when (IsGraceful(ex)) { /* graceful */ }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }
}

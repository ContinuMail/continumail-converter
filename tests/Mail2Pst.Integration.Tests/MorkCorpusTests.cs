// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class MorkCorpusTests
{
    [SkippableFact]
    public void RealMsf_Parses_AndHasReadUnreadMix()
    {
        string? path = Environment.GetEnvironmentVariable("MAIL2PST_MORK_CORPUS_MSF");
        Skip.If(string.IsNullOrEmpty(path), "MAIL2PST_MORK_CORPUS_MSF not set");
        Skip.IfNot(File.Exists(path), $"MAIL2PST_MORK_CORPUS_MSF points to a missing file: {path}");

        MorkDocument doc = MorkReader.Parse(path!);
        Assert.True(doc.TryGetSingleTable("ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out MorkTable msgs));

        // Aggregate-only. NEVER assert/print subject/sender text (PII).
        Assert.True(msgs.Rows.Count > 100, $"expected many rows, got {msgs.Rows.Count}");
        int unread = msgs.Rows.Values.Count(r =>
            r.TryGetCell("flags", out string f)
            && int.TryParse(f, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int fv)
            && (fv & 0x1) == 0);
        Assert.True(unread > 0, "expected at least some unread rows (flags bit 0x1 clear)");
    }
}

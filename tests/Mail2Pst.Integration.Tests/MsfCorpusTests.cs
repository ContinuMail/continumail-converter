// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class MsfCorpusTests
{
    [SkippableFact]
    public void RealMsf_Interpreted_AggregateOnly()
    {
        string? path = Environment.GetEnvironmentVariable("MAIL2PST_MORK_CORPUS_MSF");
        Skip.If(string.IsNullOrEmpty(path), "MAIL2PST_MORK_CORPUS_MSF not set");
        Skip.IfNot(File.Exists(path), $"MAIL2PST_MORK_CORPUS_MSF points to a missing file: {path}");

        MorkDocument doc = MorkReader.Parse(path!);
        MsfReadResult result = MsfMessageReader.Read(doc);

        // Core invariant: 1:1 with the msgs table rows — every row interpreted, none dropped/duplicated.
        Assert.True(doc.TryGetSingleTable(
            MsfMessageReader.MsgsScope, MsfMessageReader.MsgsKind, out MorkTable msgs));
        Assert.Equal(msgs.Rows.Count, result.Messages.Count);
        Assert.True(result.Messages.Count > 100, $"expected many messages, got {result.Messages.Count}");

        // Sanity: at least one message has a recognised flag state (proves flags actually interpreted).
        // Aggregate only — never assert/print PII (subject/sender).
        Assert.Contains(result.Messages, m => m.IsRead);

        // The read/unread MIX is a property of THIS known corpus (the same MAIL2PST_MORK_CORPUS_MSF
        // fixture MorkCorpusTests already asserts has unread rows), not of `.msf` in general — an all-read
        // folder is legitimate. Kept as a fixture expectation, not a universal invariant.
        Assert.Contains(result.Messages, m => !m.IsRead);
    }
}

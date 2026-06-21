// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.Parsing;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MboxMessageIdScannerTests
{
    // One mbox message: a "From " postmark, headers (incl. optional Message-ID), a blank line, a body.
    private static string Msg(string? messageIdHeader, string body) =>
        "From sender@example.com Thu Jan 01 00:00:00 2026\n" +
        "Subject: t\n" +
        (messageIdHeader is null ? "" : $"Message-ID: {messageIdHeader}\n") +
        "\n" + body + "\n";

    private static string WriteMbox(params string[] messages)
    {
        string path = Path.Combine(Path.GetTempPath(), "mail2pst-scan-" + System.Guid.NewGuid() + ".mbox");
        File.WriteAllText(path, string.Concat(messages));
        return path;
    }

    [Fact]
    public void ScanDuplicateIds_FlagsRepeatedIds_NotUniqueOnes()
    {
        string path = WriteMbox(Msg("<a@h>", "one"), Msg("<a@h>", "two"), Msg("<b@h>", "three"));
        try
        {
            MboxDuplicateIdSet dup = MboxMessageIdScanner.ScanDuplicateIds(path);
            Assert.True(dup.Contains("<a@h>"));
            Assert.False(dup.Contains("<b@h>"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanDuplicateIds_EmptyMbox_NoDuplicates()
    {
        string path = WriteMbox();
        try
        {
            MboxDuplicateIdSet dup = MboxMessageIdScanner.ScanDuplicateIds(path);
            Assert.False(dup.Contains("<anything@h>"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanDuplicateIds_MatchesFullParserDerivedDuplicates_ValueParity()
    {
        // Includes a comment-bearing id and a missing id to prove VALUE parity with the full parser,
        // not just boundary parity.
        string path = WriteMbox(
            Msg("<a@h>", "first"),
            Msg("<a@h>", "dup of a"),
            Msg("<b@h> (a comment)", "with comment"),
            Msg(null, "no id"));
        try
        {
            // Truth: run the REAL parser, take MailMessage.MessageId, find duplicates.
            var parserIds = new MboxParser().Parse(path)
                .Where(r => r.Success).Select(r => r.Message!.MessageId).OfType<string>().ToList();
            var expectedDup = parserIds.GroupBy(x => x, System.StringComparer.Ordinal)
                .Where(g => g.Count() > 1).Select(g => g.Key)
                .ToHashSet(System.StringComparer.Ordinal);

            MboxDuplicateIdSet dup = MboxMessageIdScanner.ScanDuplicateIds(path);

            foreach (string id in parserIds.Distinct())
                Assert.Equal(expectedDup.Contains(id), dup.Contains(id));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanDuplicateIds_MalformedHeaderChunk_DoesNotAbort()
    {
        // A message whose header block is junk must not abort the pre-pass; valid messages still count.
        string junk = "From s@e Thu Jan 01 00:00:00 2026\n!!! not a header !!!\n\nbody\n";
        string path = WriteMbox(junk, Msg("<dup@h>", "a"), Msg("<dup@h>", "b"));
        try
        {
            MboxDuplicateIdSet dup = MboxMessageIdScanner.ScanDuplicateIds(path);
            Assert.True(dup.Contains("<dup@h>"));
        }
        finally { File.Delete(path); }
    }
}

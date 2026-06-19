// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Mork;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class CategoryRoundTripTests
{
    private static string NewOutDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "mail2pst-cat-" + Guid.NewGuid());
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Categories_RoundTrip_IncludingNonAscii_AndFlagsPreserved()
    {
        var msgs = new List<MailMessage>
        {
            new() { MessageId = "<c1@h>", Subject = "one", IsRead = true, IsFlagged = true,
                    Categories = { "Work", "Important", "Ældre" } },
            new() { MessageId = "<c2@h>", Subject = "two", Categories = { "Junk" } },
            new() { MessageId = "<c3@h>", Subject = "three" }, // no categories
        };
        string outDir = NewOutDir();
        try
        {
            var outputs = RoundTripHarness.ConvertMessages(msgs, outDir);
            IReadOnlyList<ReadBackMessage> read = PstReader.Read(outputs).SelectMany(f => f.Messages).ToList();
            ReadBackMessage By(string id) => read.Single(m => m.MessageId == id);

            Assert.Equal(new[] { "Work", "Important", "Ældre" }, By("<c1@h>").Categories);
            Assert.True(By("<c1@h>").IsRead);
            Assert.True(By("<c1@h>").IsFlagged);
            Assert.Equal(new[] { "Junk" }, By("<c2@h>").Categories);
            Assert.Empty(By("<c3@h>").Categories);
        }
        finally { Directory.Delete(outDir, true); }
    }

    [Fact]
    public void JunkCategory_AddedByEnricher_RoundTrips()
    {
        // Proves the SP3 path end-to-end: the ENRICHER adds "Junk" (JunkHandling.Category), not a manual preload.
        var mail = new MailMessage { MessageId = "<jr@h>", Subject = "junky" };
        var row = new MorkRow("1", new Dictionary<string, string>(StringComparer.Ordinal)
            { ["message-id"] = "jr@h", ["junkscore"] = "90", ["keywords"] = "work" });
        var table = new MorkTable("1", MsfMessageReader.MsgsScope, MsfMessageReader.MsgsKind,
            new Dictionary<string, MorkRow> { ["1"] = row });
        MsfReadResult msf = MsfMessageReader.Read(new MorkDocument(new[] { table }));

        MsfEnricher.Enrich(new[] { mail }, msf,
            new MsfEnrichmentOptions { JunkHandling = JunkHandlingMode.Category });

        string outDir = NewOutDir();
        try
        {
            var outputs = RoundTripHarness.ConvertMessages(new[] { mail }, outDir);
            ReadBackMessage read = PstReader.Read(outputs).SelectMany(f => f.Messages).Single();
            Assert.Contains("Junk", read.Categories);
            Assert.Contains("work", read.Categories);
        }
        finally { Directory.Delete(outDir, true); }
    }
}

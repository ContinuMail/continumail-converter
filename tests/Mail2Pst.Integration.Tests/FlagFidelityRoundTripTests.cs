// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class FlagFidelityRoundTripTests
{
    private static ConversionConfig MirrorConfig(string fixtureFileName) => new()
    {
        Outputs = new List<OutputGroupConfig>
        {
            new()
            {
                Name = "Personal", MaxSizeMB = 100, FolderMapping = FolderMappingMode.Mirror,
                Sources = new List<SourceConfig>
                {
                    new() { Type = "mbox", Path = Path.Combine(RoundTripHarness.FixturesDir, fixtureFileName) },
                },
            },
        },
    };

    [Fact]
    public void Flags_RoundTrip_FromMboxStatusBits()
    {
        ConversionConfig config = MirrorConfig("mbox-with-flags.mbox");
        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-flags-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        try
        {
            var (outputs, _) = RoundTripHarness.Convert(config, outDir);
            IReadOnlyList<ReadBackMessage> msgs =
                PstReader.Read(outputs).SelectMany(f => f.Messages).ToList();

            // Catch fixture-parsing/boundary mistakes before the per-message Single(...) asserts.
            Assert.Equal(6, msgs.Count);

            ReadBackMessage By(string id) => msgs.Single(m => m.MessageId == id);

            ReadBackMessage read = By("<flag-read@example.com>");
            Assert.True(read.IsRead);
            Assert.False(read.IsReplied || read.IsForwarded || read.IsFlagged);

            ReadBackMessage unread = By("<flag-unread@example.com>");
            Assert.False(unread.IsRead);
            Assert.False(unread.IsReplied || unread.IsForwarded || unread.IsFlagged);

            ReadBackMessage starred = By("<flag-starred@example.com>");
            Assert.True(starred.IsRead);
            Assert.True(starred.IsFlagged);
            Assert.False(starred.IsReplied || starred.IsForwarded);

            ReadBackMessage replied = By("<flag-replied@example.com>");
            Assert.True(replied.IsRead);
            Assert.True(replied.IsReplied);
            Assert.False(replied.IsForwarded || replied.IsFlagged);

            ReadBackMessage forwarded = By("<flag-forwarded@example.com>");
            Assert.True(forwarded.IsRead);
            Assert.True(forwarded.IsForwarded);
            Assert.False(forwarded.IsReplied || forwarded.IsFlagged);

            // Documented lossy mapping: single-valued PidTagLastVerbExecuted -> reply (102) wins,
            // so a replied+forwarded message reads back as replied, NOT forwarded.
            ReadBackMessage both = By("<flag-replied-forwarded@example.com>");
            Assert.True(both.IsRead);
            Assert.True(both.IsReplied);
            Assert.False(both.IsForwarded);
            Assert.False(both.IsFlagged);
        }
        finally { Directory.Delete(outDir, true); }
    }
}

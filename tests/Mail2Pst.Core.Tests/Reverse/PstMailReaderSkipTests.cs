// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class PstMailReaderSkipTests
{
    [Fact]
    public void ReportMessageReadFailure_WithOnSkipped_RecordsStructuredSkip_NoDuplicateWarning()
    {
        var skips = new List<ExportSkip>();
        var warns = new List<string>();
        PstMailReader.ReportMessageReadFailure(
            new[] { "Parent", "Inbox" }, 4, new InvalidDataException("bad node"), warns.Add, skips.Add);

        ExportSkip s = Assert.Single(skips);
        Assert.Empty(warns);                                   // NOT double-reported as a warning
        Assert.Equal("Parent / Inbox", s.FolderPath);
        Assert.Equal(4, s.MessageIndex);
        Assert.Contains("InvalidDataException", s.Reason);     // (iii) type name + message
        Assert.Contains("bad node", s.Reason);
    }

    [Fact]
    public void ReportMessageReadFailure_WithoutOnSkipped_EmitsLegacyWarningOnly()
    {
        var warns = new List<string>();
        PstMailReader.ReportMessageReadFailure(
            new[] { "Inbox" }, 2, new IOException("io"), warns.Add, onSkipped: null);

        string w = Assert.Single(warns);
        Assert.Contains("skipped message 2 in 'Inbox'", w);
        Assert.Contains("IOException", w);
    }
}

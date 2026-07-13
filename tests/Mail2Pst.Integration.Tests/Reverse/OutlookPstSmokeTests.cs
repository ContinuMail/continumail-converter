// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Linq;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Integration.Tests.Reverse;

[Trait("Category", "OutlookPstSmoke")]
public class OutlookPstSmokeTests
{
    [SkippableFact]
    public void Read_RealOutlookPst_ResolvesRootAndReconstructsMessages()
    {
        string? pst = Environment.GetEnvironmentVariable("MAIL2PST_OUTLOOK_PST");
        Skip.If(string.IsNullOrEmpty(pst),
            "Set MAIL2PST_OUTLOOK_PST to a real Outlook-authored .pst to run the reverse-reader smoke.");

        var items = PstMailReader.EnumerateMessages(pst!, _ => { }).ToList();

        Assert.NotEmpty(items);                                     // TopOfPersonalFolders found the tree
        Assert.Contains(items, it =>                                // at least one message reconstructs
            !string.IsNullOrEmpty(it.Message.Subject)
            || !string.IsNullOrEmpty(it.Message.PlainBody)
            || (it.Message.HtmlBody is { Length: > 0 }));
    }
}

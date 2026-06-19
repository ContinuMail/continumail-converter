// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class RoundTripComparerTests
{
    private static MailMessage Msg(string id, string subject) =>
        new() { MessageId = id, Subject = subject, TextBody = "body", From = new MailAddress { Email = "a@b.com" } };

    private static ReadBackMessage Read(string id, string subject) =>
        new(subject, "a@b.com", Array.Empty<ReadRecipient>(), null, Array.Empty<string>(), true, id, false, false, false, false, Array.Empty<string>());

    [Fact]
    public void AssertRoundTrip_MetadataMismatch_Throws()
    {
        var truth = new Dictionary<string, List<MailMessage>> { ["Inbox"] = new() { Msg("m1", "Hello") } };
        var readback = new List<ReadFolder> { new(new[] { "Inbox" }, new[] { Read("m1", "WRONG SUBJECT") }) };
        Assert.ThrowsAny<Exception>(() => RoundTripComparer.AssertRoundTrip(truth, readback));
    }

    [Fact]
    public void AssertRoundTrip_FolderCountMismatch_Throws()
    {
        var truth = new Dictionary<string, List<MailMessage>> { ["Inbox"] = new() { Msg("m1", "A"), Msg("m2", "B") } };
        var readback = new List<ReadFolder> { new(new[] { "Inbox" }, new[] { Read("m1", "A") }) };
        Assert.ThrowsAny<Exception>(() => RoundTripComparer.AssertRoundTrip(truth, readback));
    }

    [Fact]
    public void AssertRoundTrip_FolderSetMismatch_Throws()
    {
        var truth = new Dictionary<string, List<MailMessage>> { ["Inbox"] = new() { Msg("m1", "A") } };
        var readback = new List<ReadFolder> { new(new[] { "Archive" }, new[] { Read("m1", "A") }) };
        Assert.ThrowsAny<Exception>(() => RoundTripComparer.AssertRoundTrip(truth, readback));
    }

    [Fact]
    public void AssertRoundTrip_FullMatch_Passes()
    {
        var truth = new Dictionary<string, List<MailMessage>> { ["Inbox"] = new() { Msg("m1", "A") } };
        var readback = new List<ReadFolder> { new(new[] { "Inbox" }, new[] { Read("m1", "A") }) };
        RoundTripComparer.AssertRoundTrip(truth, readback); // must not throw
    }
}

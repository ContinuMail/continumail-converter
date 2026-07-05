// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class OutlookDetectionTests
{
    [Theory]
    [InlineData("Cannot complete the operation. You are not connected.", true)]
    [InlineData("The server is not available. Contact your administrator if this condition persists.", false)]
    [InlineData("anything else", false)]
    public void Classifies_interactive_logon_required(string message, bool expected) =>
        Assert.Equal(expected, OutlookDetection.LooksLikeInteractiveLogonRequired(new InvalidOperationException(message)));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ContinuMail", true)]
    [InlineData("My Profile", true)]
    [InlineData(@"bad\name", false)]
    [InlineData("bad/name", false)]
    [InlineData("bad\"name", false)]
    [InlineData("bad'name", false)] // [R3:1] validator rejects ' too — pin the stated rule
    [InlineData("bad\tname", false)]
    public void Validates_profile_names(string? name, bool valid) =>
        Assert.Equal(valid, OutlookDetection.ValidateProfileName(name) is null);

    [Fact]
    public void Rejects_overlong_names() =>
        Assert.NotNull(OutlookDetection.ValidateProfileName(new string('a', 65)));
}

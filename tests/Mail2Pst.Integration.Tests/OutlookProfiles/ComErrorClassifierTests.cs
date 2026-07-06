// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class ComErrorClassifierTests
{
    [Fact]
    public void CorruptXml_IsNotTransient() =>
        Assert.False(ComErrorClassifier.IsTransientOpen(new FormatException("bad xml")));

    [Fact]
    public void AccessDenied_IsNotTransient() =>
        Assert.False(ComErrorClassifier.IsTransientOpen(new UnauthorizedAccessException()));

    [Fact]
    public void UnknownError_IsNotTransientByDefault() =>
        Assert.False(ComErrorClassifier.IsTransientOpen(new Exception("mystery")));
}

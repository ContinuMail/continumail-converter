// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Parsing;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class RawMessageSpillExceptionTests
{
    [Fact]
    public void IsNotASkippableParseException()
    {
        var ex = new RawMessageSpillException("temp write failed", new IOException("disk full"));
        // The per-message skip allowlist is FormatException/IOException ONLY. Spill failures must be neither
        // (else a disk-full would be swallowed as a "skipped message" = silent data loss).
        Assert.IsNotType<IOException>(ex);
        Assert.IsNotType<FormatException>(ex);
        Assert.IsAssignableFrom<Exception>(ex);
        Assert.Contains("temp write failed", ex.Message);
    }
}

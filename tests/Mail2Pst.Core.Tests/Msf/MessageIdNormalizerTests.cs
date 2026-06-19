// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class MessageIdNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("<abc@host>", "<abc@host>")]
    [InlineData("abc@host", "<abc@host>")]
    [InlineData("  abc@host  ", "<abc@host>")]
    public void NormalizeForJoin_Cases(string? input, string? expected)
        => Assert.Equal(expected, MessageIdNormalizer.NormalizeForJoin(input));
}

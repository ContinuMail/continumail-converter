// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkDepthLimitTests
{
    [Fact]
    public void Parse_DeeplyNestedDicts_ThrowsMorkFormatException_NotStackOverflow()
    {
        // 128 balanced-nested empty dicts (<<<...>>>). This is past the 64-level ceiling but
        // shallow enough that the PRE-fix parser recurses without a real StackOverflowException
        // (which would be uncatchable and kill the test host). The fixed parser must throw the
        // ordinary catchable MorkFormatException at the ceiling.
        string deep = new string('<', 128) + new string('>', 128);

        Assert.Throws<MorkFormatException>(() => MorkReader.ParseString(deep));
    }
}

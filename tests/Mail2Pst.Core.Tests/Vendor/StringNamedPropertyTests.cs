// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Vendor;

public class StringNamedPropertyTests
{
    [Fact]
    public void MultiString_Serializer_RoundTrips_IncludingNonAscii()
    {
        var values = new List<string> { "Work", "Important", "Ældre" };
        byte[] blob = PropertyContext.SerializeMultiString(values);
        Assert.Equal(values, PropertyContext.DeserializeMultiString(blob));
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryStreamBytesTests
{
    [Fact]
    public void ByteArray_PassesThroughUnchanged()
    {
        var input = new byte[] { 0x3C, 0x3F, 0xFF, 0x80, 0x00 };
        Assert.Same(input, CategoryStreamBytes.FromVariant(input));
    }

    [Fact]
    public void SByteArray_ReinterpretsHighBitBytes_NoOverflow()
    {
        // VT_I1 marshaling: source bytes 0x00, 0x7F, 0x80, 0xFF arrive as sbyte 0, 127, -128, -1.
        var input = new sbyte[] { 0, 127, -128, -1 };
        Assert.Equal(new byte[] { 0x00, 0x7F, 0x80, 0xFF }, CategoryStreamBytes.FromVariant(input));
    }

    [Fact]
    public void ShortArray_ReinterpretsLowByte()
    {
        var input = new short[] { 0x00C6, 0x00D8 }; // Æ, Ø low bytes
        Assert.Equal(new byte[] { 0xC6, 0xD8 }, CategoryStreamBytes.FromVariant(input));
    }

    [Theory]
    [InlineData(null)]
    public void NullOrDbNull_Throws(object? raw)
    {
        Assert.Throws<InvalidOperationException>(() => CategoryStreamBytes.FromVariant(raw));
        Assert.Throws<InvalidOperationException>(() => CategoryStreamBytes.FromVariant(DBNull.Value));
    }

    [Fact]
    public void NonBinary_Throws() =>
        Assert.Throws<InvalidOperationException>(() => CategoryStreamBytes.FromVariant("not bytes"));
}

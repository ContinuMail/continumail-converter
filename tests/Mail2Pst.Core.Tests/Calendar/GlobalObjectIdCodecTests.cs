// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

/// <summary>
/// Unit tests for <see cref="GlobalObjectIdCodec"/>.
/// All data is synthetic (Exchange GOID format verified from real Outlook dump, Task 0).
/// </summary>
public class GlobalObjectIdCodecTests
{
    // A real-looking Exchange GOID hex string (112 chars = 56 bytes).
    // Prefix: 040000008200E00074C5B7101A82E008 (EDK byte-array class id, bytes 0-15).
    // Bytes 16-19 (exception-date): 00000000 (already zero in this sample).
    private const string GoidHex =
        "040000008200E00074C5B7101A82E00800000000B40F44F359B6DC01000000000000000010000000DC21992D2F90224BB5EE6F8189627094";

    [Fact]
    public void Exchange_hex_id_decodes_to_56_byte_goid()
    {
        Assert.True(GlobalObjectIdCodec.TryDecode(GoidHex, out var goid, out var clean));
        Assert.Equal(56, goid.Length);
        Assert.Equal(Convert.FromHexString(GoidHex), goid);
        // CleanGlobalObjectId zeroes the exception-date bytes [16..20]; identical here (already zero).
        Assert.Equal(0, clean[16]); Assert.Equal(0, clean[17]); Assert.Equal(0, clean[18]); Assert.Equal(0, clean[19]);
    }

    [Fact]
    public void Mozilla_uuid_is_not_a_goid()   => Assert.False(GlobalObjectIdCodec.TryDecode("4ad842d0-1234-5678-9abc-def012345678", out _, out _));

    [Fact]
    public void Caldav_email_uid_is_not_a_goid() => Assert.False(GlobalObjectIdCodec.TryDecode("abc123@example.com", out _, out _));

    [Fact]
    public void Null_or_empty_is_not_a_goid()   => Assert.False(GlobalObjectIdCodec.TryDecode(null, out _, out _));

    [Fact]
    public void Clean_zeroes_only_the_exception_date_bytes()
    {
        // Synthetic GOID with NON-ZERO date bytes [16..20] proves the clean transform (the real sample is
        // already zero there, so it can't). Bytes 0..15 = EDK prefix; force 16..19 = AA BB CC DD.
        byte[] goid = Convert.FromHexString("040000008200E00074C5B7101A82E008" + "AABBCCDD"
            + "74D8B6378F09DD01000000000000000010000000DC21992D2F90224BB5EE6F8189627094");
        string hex = Convert.ToHexString(goid);
        Assert.True(GlobalObjectIdCodec.TryDecode(hex, out var full, out var clean));
        Assert.Equal(goid, full);                                   // full = verbatim
        for (int i = 16; i < 20; i++) Assert.Equal(0, clean[i]);    // date bytes zeroed
        for (int i = 0; i < 16; i++)  Assert.Equal(full[i], clean[i]);   // prefix untouched
        for (int i = 20; i < 56; i++) Assert.Equal(full[i], clean[i]);   // everything after untouched
    }
}

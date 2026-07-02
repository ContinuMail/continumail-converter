// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
namespace Mail2Pst.Core.Calendar;

/// <summary>
/// Detects and hex-decodes a Thunderbird <c>cal_events.id</c> that is an Exchange-cached
/// GlobalObjectId (uppercase-hex, 112 chars = 56 bytes, EDK prefix
/// <c>040000008200E00074C5B7101A82E008</c>).
///
/// Mozilla UUIDs, CalDAV UIDs, and any other string that does not start with the prefix
/// (or is not exactly 112 hex chars) return <c>false</c>.
/// </summary>
public static class GlobalObjectIdCodec
{
    private const string EdkPrefix = "040000008200E00074C5B7101A82E008";   // GOID byte-array class id (hex)

    /// <summary>
    /// Attempts to decode <paramref name="id"/> as a 56-byte Exchange GlobalObjectId.
    /// </summary>
    /// <param name="id">The raw <c>cal_events.id</c> value (may be null).</param>
    /// <param name="goid">
    /// On success: the verbatim 56-byte GOID blob (<c>PidLidGlobalObjectId</c>).
    /// On failure: <see cref="System.Array.Empty{T}()"/>.
    /// </param>
    /// <param name="cleanGoid">
    /// On success: a clone of <paramref name="goid"/> with exception-date bytes [16..20) zeroed
    /// (<c>PidLidCleanGlobalObjectId</c>). On failure: <see cref="System.Array.Empty{T}()"/>.
    /// </param>
    /// <returns><c>true</c> iff <paramref name="id"/> is a valid Exchange GOID hex string.</returns>
    public static bool TryDecode(string? id, out byte[] goid, out byte[] cleanGoid)
    {
        goid = System.Array.Empty<byte>(); cleanGoid = System.Array.Empty<byte>();
        if (id is null || id.Length != 112) return false;
        if (!id.StartsWith(EdkPrefix, System.StringComparison.OrdinalIgnoreCase)) return false;
        try { goid = System.Convert.FromHexString(id); } catch { return false; }
        if (goid.Length != 56) { goid = System.Array.Empty<byte>(); return false; }
        cleanGoid = ToCleanGlobalObjectId(goid);
        return true;
    }

    /// <summary>
    /// Returns a clone of <paramref name="goid"/> with the exception-date bytes [16..20) zeroed —
    /// the <c>PidLidCleanGlobalObjectId</c> derivation. Shared by <see cref="TryDecode"/> and the
    /// appointment writer so the derivation lives in exactly one place.
    /// </summary>
    public static byte[] ToCleanGlobalObjectId(byte[] goid)
    {
        byte[] clean = (byte[])goid.Clone();
        for (int i = 16; i < 20 && i < clean.Length; i++) clean[i] = 0;
        return clean;
    }
}

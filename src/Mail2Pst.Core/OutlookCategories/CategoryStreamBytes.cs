// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Globalization;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Normalizes the binary value a COM <c>PropertyAccessor</c> returns for the CategoryList FAI's
/// <c>PidTagRoamingXmlStream</c> into a <c>byte[]</c>. Pure (no COM types), so it is unit-testable; the CLI's
/// shim only hands it the boxed VARIANT. PT_BINARY usually marshals as <c>byte[]</c>, but some Outlook builds
/// yield a 1-D <c>Array</c> of another integral element type (e.g. <c>sbyte[]</c>, VT_I1).
/// </summary>
public static class CategoryStreamBytes
{
    public static byte[] FromVariant(object? raw)
    {
        switch (raw)
        {
            case null:
            case DBNull:
                throw new InvalidOperationException("The category list stream is empty or missing.");
            case byte[] b:
                return b;
            case Array a:
                // Reinterpret each element's low 8 bits. Convert.ToByte would throw OverflowException for any
                // sbyte < 0 (i.e. any source byte >= 0x80, which non-ASCII UTF-8 — like "ÆØÅ" — guarantees).
                var bytes = new byte[a.Length];
                for (int i = 0; i < a.Length; i++)
                    bytes[i] = unchecked((byte)Convert.ToInt64(a.GetValue(i), CultureInfo.InvariantCulture));
                return bytes;
            default:
                throw new InvalidOperationException(
                    $"The category list stream is not binary (got {raw.GetType().Name}).");
        }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Parsing;

public static class ParserRegistry
{
    private static readonly Dictionary<string, IMailSourceParser> Parsers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mbox"] = new MboxParser(),
        };

    private static readonly Dictionary<string, IMailSourceParser> ScanParsers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mbox"] = new MboxParser(measureOnly: true, rawSpillThreshold: 4L * 1024 * 1024),
        };

    public static IMailSourceParser Get(string sourceType)
    {
        if (Parsers.TryGetValue(sourceType, out IMailSourceParser? parser))
        {
            return parser;
        }

        throw new NotSupportedException($"Unsupported source type: '{sourceType}'");
    }

    /// <summary>Resolves the measure-only parser for scan (length-only attachments, no retained bytes,
    /// no temp files). Same unsupported-type contract as <see cref="Get"/>.</summary>
    public static IMailSourceParser GetForScan(string sourceType)
    {
        if (ScanParsers.TryGetValue(sourceType, out IMailSourceParser? parser))
            return parser;
        throw new NotSupportedException($"Unsupported source type: '{sourceType}'");
    }
}

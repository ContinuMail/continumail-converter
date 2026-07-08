// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Cli;
using Mail2Pst.Core.Scanning;

namespace Mail2Pst.Cli;

internal static class ScanCommand
{
    internal static int Run(string[] args)
    {
        List<string> inputPaths = CliArgs.Flags(args, "--input");
        string? inputListPath = CliArgs.Flag(args, "--input-list");
        string sourceType = CliArgs.Flag(args, "--type") ?? "mbox";
        bool streaming = CliArgs.HasFlag(args, "--progress");

        // --input-list: one path per line (blank lines ignored). Lets callers with
        // hundreds of sources stay under the Windows 32,767-char command-line limit.
        if (inputListPath is not null)
        {
            if (!File.Exists(inputListPath))
            {
                Console.Error.WriteLine($"Input list file not found: {inputListPath}");
                return 1;
            }
            foreach (string line in File.ReadAllLines(inputListPath))
            {
                if (!string.IsNullOrWhiteSpace(line)) inputPaths.Add(line.Trim());
            }
        }

        if (inputPaths.Count == 0)
        {
            Console.Error.WriteLine("Usage: continumail-convert scan --input <path> [--input <path> ...] [--input-list <file>] [--type mbox]");
            return 1;
        }

        foreach (string inputPath in inputPaths)
        {
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Input file not found: {inputPath}");
                return 1;
            }
        }

        try
        {
            var scanRunner = new ScanRunner();

            Action<ScanProgress>? onProgress = streaming
                ? p => CliArgs.WriteJsonLine(new { type = "scanProgress", bytes = p.Bytes, totalBytes = p.TotalBytes })
                : null;

            ScanReport report = scanRunner.Scan(inputPaths, sourceType, onProgress);

            static string? Iso(DateTimeOffset? d) =>
                d?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            var output = new
            {
                type = "scan",
                totals = new
                {
                    messages = report.Totals.Messages,
                    bytes = report.Totals.Bytes,
                    sourceBytes = report.Totals.SourceBytes,
                    sources = report.Totals.Sources,
                },
                sources = report.Sources.Select(s => new
                {
                    id = s.Id,
                    path = s.Path,
                    displayName = s.DisplayName,
                    messages = s.Messages,
                    bytes = s.EstimatedBytes,
                    sourceBytes = s.SourceBytes,
                    dateFrom = Iso(s.DateFrom),
                    dateTo = Iso(s.DateTo),
                    warnings = s.Warnings,
                    skipped = s.Skipped,
                }),
                skipped = report.Skipped.Select(s => new { source = s.SourcePath, identifier = s.Identifier, reason = s.Reason }),
                warnings = report.Warnings.Select(w => new { source = w.SourcePath, identifier = w.Identifier, reason = w.Reason }),
            };

            Console.WriteLine(CliEventSerializer.Serialize(output, indented: !streaming));

            return 0;
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine($"Unsupported source type '{sourceType}': {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}

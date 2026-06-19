// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Cli;
using Mail2Pst.Core.Discovery;

namespace Mail2Pst.Cli;

internal static class DiscoverCommand
{
    internal static int Run(string[] args)
    {
        string? input = CliArgs.Flag(args, "--input");
        if (input is null)
        {
            Console.Error.WriteLine("Usage: continumail-convert discover --input <dir>");
            return 1;
        }

        if (!Directory.Exists(input)) // false for a missing path AND for a file path
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "discover", message = $"Input directory not found: {input}", fatal = true });
            Console.Error.WriteLine($"Input directory not found: {input}");
            return 1;
        }

        try
        {
            DiscoveryResult r = MailProfileDiscovery.Discover(input);
            var output = new
            {
                type = "discovery",
                root = r.Root,
                layout = r.Layout,
                sources = r.Sources.Select(s => new
                {
                    path = s.Path, type = s.Type, targetFolderPath = s.TargetFolderPath,
                    displayName = s.DisplayName, sourceBytes = s.SourceBytes, msfPath = s.MsfPath,
                }),
                warnings = r.Warnings.Select(w => new
                {
                    code = w.Code, path = w.Path, targetFolderPath = w.TargetFolderPath,
                    segment = w.Segment, segmentIndex = w.SegmentIndex, relatedPaths = w.RelatedPaths, message = w.Message,
                }),
                skipped = r.Skipped.Select(s => new { code = s.Code, path = s.Path, reason = s.Reason }),
                pairing = new
                {
                    pairedMsfCount = r.Pairing.PairedMsfCount,
                    unpairedMboxCount = r.Pairing.UnpairedMboxCount,
                    orphanMsfCount = r.Pairing.OrphanMsfCount,
                },
            };
            Console.WriteLine(CliEventSerializer.Serialize(output, indented: true));
            return 0;
        }
        catch (Exception ex)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "discover", message = ex.Message, fatal = true });
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
    }
}

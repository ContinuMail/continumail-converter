// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Reverse;

namespace Mail2Pst.Cli;

/// <summary>
/// The reverse `export` subcommand: reads one PST and writes a Thunderbird-importable mbox tree under
/// --output. Mirrors the forward <see cref="ConvertCommand"/> and reuses the same JSON-Lines contract
/// (via <see cref="CliArgs.WriteJsonLine"/> → CliEventSerializer, so every event carries schemaVersion).
/// Every event is tagged command="export" so a consumer can discriminate it from the forward stream.
/// No config.json (one PST → one tree); no cancellation (the reverse runner has no cancellation contract).
/// </summary>
internal static class ExportCommand
{
    internal static int Run(string[] args)
    {
        string? input = CliArgs.Flag(args, "--input");
        string? output = CliArgs.Flag(args, "--output");
        bool includeEmpty = CliArgs.HasFlag(args, "--include-empty");

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            const string msg = "export requires --input <path.pst> and --output <dir>.";
            CliArgs.WriteJsonLine(new { type = "error", command = "export", stage = "export", message = msg, fatal = true });
            Console.Error.WriteLine(msg);
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  continumail-convert export --input <path.pst> --output <dir> [--include-empty]");
            return 1;
        }

        if (!File.Exists(input))
        {
            string msg = $"Input PST not found: {input}";
            CliArgs.WriteJsonLine(new { type = "error", command = "export", stage = "export", message = msg, fatal = true });
            Console.Error.WriteLine(msg);
            return 1;
        }

        CliArgs.WriteJsonLine(new { type = "started", command = "export", input, outputDirectory = output, includeEmpty });

        var runner = new PstExportRunner();
        ExportReport report = runner.Run(
            input, output, includeEmpty,
            onWarning: w => CliArgs.WriteJsonLine(BuildWarningEvent(w)),
            onProgress: p => CliArgs.WriteJsonLine(BuildProgressEvent(p)),
            onSkipped: s => CliArgs.WriteJsonLine(BuildSkippedEvent(s)));

        CliArgs.WriteJsonLine(new
        {
            type = "done", command = "export",
            messagesExported = report.MessagesExported,
            foldersWritten = report.FoldersWritten,
            mboxFileCount = report.MboxFiles.Count,
            skipped = report.SkippedCount,
            warnings = report.WarningCount,
            outputs = new[] { report.OutputRoot },
            outputDirectory = report.OutputRoot,
            elapsedMs = (long)report.Elapsed.TotalMilliseconds,
        });
        return 0;
    }

    // Event factories: one place that owns the discriminated wire shape of each streamed event, so a
    // skip and a warning are provably disjoint and each is unit-testable without a PST.
    internal static object BuildWarningEvent(string message) =>
        new { type = "warning", command = "export", message };

    internal static object BuildProgressEvent(ExportProgress p) =>
        new { type = "progress", command = "export", messagesExported = p.MessagesExported, currentFolder = p.CurrentFolder };

    internal static object BuildSkippedEvent(ExportSkip s) =>
        new { type = "skipped", command = "export", folderPath = s.FolderPath, messageIndex = s.MessageIndex, reason = s.Reason };
}

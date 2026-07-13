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
    private const string ReportJsonSuffix = ".export-report.json";
    private const string ReportTxtSuffix = ".export-report.txt";

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
        ExportReport report;
        try
        {
            report = runner.Run(
                input, output, includeEmpty,
                onWarning: w => CliArgs.WriteJsonLine(BuildWarningEvent(w)),
                onProgress: p => CliArgs.WriteJsonLine(BuildProgressEvent(p)),
                onSkipped: s => CliArgs.WriteJsonLine(BuildSkippedEvent(s)));
        }
        catch (Exception ex)
        {
            CliArgs.WriteJsonLine(new { type = "error", command = "export", stage = "export", message = ex.Message, fatal = true });
            Console.Error.WriteLine($"Export failed: {ex.Message}");
            return 1;
        }

        // The mbox tree is now PUBLISHED at report.OutputRoot. Report files go BESIDE the tree, never inside
        // it: a PST folder can legally be named "export-report.json", and writing into the tree would clobber
        // that mailbox. Refuse pre-existing destinations, write temps, then move each into place WITHOUT
        // overwrite; if the second move fails, roll back the first. A report failure leaves the tree intact
        // and never overwrites a pre-existing file.
        string reportBase = Path.TrimEndingDirectorySeparator(report.OutputRoot);
        string reportJsonPath = reportBase + ReportJsonSuffix;
        string reportTxtPath = reportBase + ReportTxtSuffix;
        string tmpJson = reportJsonPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string tmpTxt = reportTxtPath + ".tmp-" + Guid.NewGuid().ToString("N");
        bool jsonPublished = false;
        try
        {
            if (PathExists(reportJsonPath) || PathExists(reportTxtPath))
                throw new IOException($"Export report destinations already exist beside '{report.OutputRoot}'.");
            File.WriteAllText(tmpJson, report.ToJson());
            File.WriteAllText(tmpTxt, report.ToSummary());
            File.Move(tmpJson, reportJsonPath);
            jsonPublished = true;
            File.Move(tmpTxt, reportTxtPath);
        }
        catch (Exception ex)
        {
            TryDelete(tmpJson);
            TryDelete(tmpTxt);
            if (jsonPublished) TryDelete(reportJsonPath);   // roll back the first final file on a partial publish
            CliArgs.WriteJsonLine(new
            {
                type = "error", command = "export", stage = "report", message = ex.Message, fatal = true,
                outputDirectory = report.OutputRoot, outputPreserved = Directory.Exists(report.OutputRoot),
            });
            Console.Error.WriteLine($"Report write failed (mbox tree preserved): {ex.Message}");
            return 1;
        }

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
            report = reportJsonPath,
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

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup; the published tree is what matters */ }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.OutlookCategories;
using Mail2Pst.Core.Progress;

namespace Mail2Pst.Cli;

internal static class ConvertCommand
{
    internal static int Run(string[] args)
    {
        ConvertResolution resolved = ConvertInput.Resolve(args);
        if (resolved.Error is not null)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "convert", message = resolved.Error, fatal = true });
            Console.Error.WriteLine(resolved.Error);
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  continumail-convert convert --config <config.json> --output <dir>");
            Console.Error.WriteLine("  continumail-convert convert --profile <dir> [--config <options.json>] --output <dir>");
            return 1;
        }

        string outputDir = resolved.OutputDir!;
        ConversionConfig config = resolved.Config!;

        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "convert", message = $"Failed to create output directory: {ex.Message}", fatal = true });
            Console.Error.WriteLine($"Failed to create output directory: {ex.Message}");
            return 1;
        }

        CliArgs.WriteJsonLine(new { type = "started", input = resolved.InputLabel, outputDirectory = outputDir });

        // Output PSTs are built from scratch by PSTFile.CreateEmptyStore — no template seed
        // is copied or read, so nothing needs to be extracted here.
        var runner = new ConversionRunner();

        using var cts = new CancellationTokenSource();

        // Trigger 1 (the Tauri-on-Windows path): a "cancel" line on stdin. Background,
        // non-blocking, and quiet when stdin is closed/redirected/non-interactive.
        var stdinReader = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = Console.In.ReadLine()) is not null)
                {
                    if (string.Equals(line.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
                    {
                        cts.Cancel();
                        return;
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true };
        stdinReader.Start();

        // Trigger 2: Ctrl+C / SIGINT. e.Cancel = true prevents the runtime's hard exit
        // so cleanup can run.
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // Trigger 3: SIGTERM (graceful termination from a parent process / manual CLI).
        // PosixSignal.SIGTERM is supported cross-platform by the .NET runtime — on Windows
        // it maps to the native termination path — so no #if/OS guard is needed here.
        // ctx.Cancel = true requests a graceful stop.
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            cts.Cancel();
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var report = runner.Run(config, outputDir, evt =>
            {
                switch (evt)
                {
                    case ScanEvent s:
                        CliArgs.WriteJsonLine(new { type = "scan", totalMessages = s.TotalMessages });
                        break;
                    case ProgressEvent p:
                        CliArgs.WriteJsonLine(new { type = "progress", converted = p.Converted, total = p.TotalMessages,
                            warnings = p.Warnings, skipped = p.Skipped, bytes = p.EstimatedOutputBytes,
                            currentSource = p.CurrentSource, currentFolder = p.CurrentFolder,
                            // additive, non-breaking (schemaVersion stays 1):
                            contactsConverted = p.ContactsConverted,
                            contactsTotal = p.ContactsTotal,
                            phase = p.Phase,
                            appointmentsConverted = p.AppointmentsConverted,
                            appointmentsTotal = p.AppointmentsTotal,
                            tasksConverted = p.TasksConverted,
                            tasksTotal = p.TasksTotal });
                        break;
                    case WarningEvent w:
                        CliArgs.WriteJsonLine(new { type = "warning", source = w.Source, identifier = w.Identifier, reason = w.Reason });
                        break;
                }
            }, cts.Token, precomputedTotalMessages: resolved.ExpectedTotal, skipTasks: resolved.NoTasks, skipAppointments: resolved.NoAppointments);

            stopwatch.Stop();

            if (report.Cancelled)
            {
                // Terminal and mutually exclusive with `done`: do not write the success
                // reports. Surface what was deleted and what completed parts remain.
                CliArgs.WriteJsonLine(new
                {
                    type = "cancelled",
                    deleted = report.DeletedFiles,
                    outputs = report.OutputFiles,
                    converted = report.ConvertedCount,
                    skipped = report.SkippedCount,
                    warnings = report.WarningCount,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                });
                return 2;
            }

            string reportJsonPath = Path.Combine(outputDir, "conversion-report.json");
            File.WriteAllText(reportJsonPath, report.ToJson());

            string reportTxtPath = Path.Combine(outputDir, "conversion-report.txt");
            File.WriteAllText(reportTxtPath, report.ToSummary());

            var colourPlan = BuildColourPlan(config.ProfilePath);

            CliArgs.WriteJsonLine(new
            {
                type = "done",
                converted = report.ConvertedCount,
                skipped = report.SkippedCount,
                warnings = report.WarningCount,
                outputs = report.OutputFiles,
                outputDirectory = outputDir,
                report = reportJsonPath,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                enrichment = report.EnrichmentSummary,
                colourPlan = colourPlan,
                // additive contact summary fields (non-breaking; schemaVersion stays 1):
                contactsConverted = report.ContactsConverted,
                contactsSkipped = report.ContactsSkipped,
                contactWarnings = report.ContactWarningCount,
                // additive appointment/task summary fields (non-breaking; schemaVersion stays 1):
                appointmentsConverted = report.AppointmentsConverted,
                appointmentsSkipped = report.AppointmentsSkipped,
                appointmentWarnings = report.AppointmentWarningCount,
                tasksConverted = report.TasksConverted,
                tasksSkipped = report.TasksSkipped,
                taskWarnings = report.TaskWarningCount,
            });

            return 0;
        }
        catch (ConfigValidationException ex)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "convert", message = ex.Message, fatal = true });
            Console.Error.WriteLine($"Config error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "convert", message = ex.Message, fatal = true });
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            // No temp template to clean up — output is created from scratch.
        }
    }

    private static object[] BuildColourPlan(string? profilePath)
    {
        if (string.IsNullOrEmpty(profilePath)) return Array.Empty<object>();
        string prefsPath = Path.Combine(profilePath, "prefs.js");
        if (!File.Exists(prefsPath)) return Array.Empty<object>();
        string content;
        try { content = File.ReadAllText(prefsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Array.Empty<object>(); }

        var plan = CategoryColorPlan.Build(
            PrefsTagReader.ParseText(content),
            PrefsTagReader.ParseColors(content));

        var list = new List<object>();
        foreach (var c in plan)
            list.Add(new { name = c.Name, hex = c.Hex, outlookColor = c.OutlookColor, action = c.Action });
        return list.ToArray();
    }
}

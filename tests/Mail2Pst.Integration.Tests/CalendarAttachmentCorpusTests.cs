// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Opt-in real-corpus smoke test for the calendar attachment pipeline (Task 6).
///
/// Set <c>MAIL2PST_CALENDAR_CORPUS</c> to the full path of a Thunderbird
/// <c>local.sqlite</c> (or <c>cache.sqlite</c>) to enable. Skipped in CI (no corpus, no PII).
/// Mirrors the <c>VCardCorpusTests.RealAddressBook_…</c> env-var + early-return pattern exactly.
///
/// Asserts:
/// <list type="bullet">
///   <item>Conversion completes without exception.</item>
///   <item>At least one appointment or task is converted.</item>
///   <item>All emitted warnings match a known structured pattern (no raw exception traces).</item>
/// </list>
/// Reserved <c>@example.com</c> only in committed fixtures — this test emits no PII.
/// </summary>
public class CalendarAttachmentCorpusTests
{
    // Known structured warning prefixes produced by the attachment / relation / recurrence pipeline.
    // Any warning not matching one of these patterns is treated as an unstructured leak (test failure).
    private static readonly Regex[] StructuredWarningPatterns =
    {
        // CalendarAttachmentResolver — attachment parse / remote / local-file warnings
        new Regex(@"attachment on '.*':", RegexOptions.IgnoreCase),
        new Regex(@"remote URL preserved as link", RegexOptions.IgnoreCase),
        new Regex(@"local file (outside|missing|unreadable|symlink)", RegexOptions.IgnoreCase),
        // ConversionRunner appointment/task skip lines
        new Regex(@"Appointment skipped \[", RegexOptions.IgnoreCase),
        new Regex(@"Task skipped \[", RegexOptions.IgnoreCase),
        // CalendarEventMapper / CalendarTaskMapper structured degrades
        new Regex(@"skipping recurring (event|task)", RegexOptions.IgnoreCase),
        new Regex(@"exception.*skipped", RegexOptions.IgnoreCase),
        new Regex(@"ignored.*change flag", RegexOptions.IgnoreCase),
        new Regex(@"RRULE.*unparseable", RegexOptions.IgnoreCase),
        new Regex(@"unparseable.*RRULE", RegexOptions.IgnoreCase),
        new Regex(@"task recurrence.*warn", RegexOptions.IgnoreCase),
        // Relation lines preserved in body
        new Regex(@"relation not natively converted", RegexOptions.IgnoreCase),
    };

    [Fact]
    public void RealCalendarCorpus_ConvertsCleansAndStructuredWarnings()
    {
        // Opt-in: set MAIL2PST_CALENDAR_CORPUS to a real local.sqlite or cache.sqlite path.
        // CI without it passes silently — same early-return pattern as VCardCorpusTests.
        string? storePath = Environment.GetEnvironmentVariable("MAIL2PST_CALENDAR_CORPUS");
        if (string.IsNullOrWhiteSpace(storePath)) return;
        if (!File.Exists(storePath)) return;   // misconfigured path — skip silently

        // CalId="" reads all calendars in the store. ConfigValidator requires explicit folder
        // paths when CalId is empty — use simple single-folder paths for both item types.
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name      = "CorpusCalendar",
                    MaxSizeMB = 50_000,
                    Sources   = new List<SourceConfig>(),
                    Contacts  = new List<ContactSourceConfig>(),
                    Calendars = new List<CalendarSourceConfig>
                    {
                        new()
                        {
                            StorePath            = storePath,
                            CalId                = "",   // "" = all calendars in store
                            IncludeAppointments  = true,
                            IncludeTasks         = true,
                            AppointmentFolderPath = new[] { "Calendar" },
                            TaskFolderPath        = new[] { "Tasks" },
                        },
                    },
                },
            },
        };

        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-cal-corpus-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        try
        {
            // Conversion must complete without exception.
            var (_, report) = RoundTripHarness.Convert(config, outDir);

            // Must have converted at least one appointment or task.
            int calCount = report.AppointmentsConverted + report.TasksConverted;
            Assert.True(calCount > 0,
                $"Expected >= 1 appointment or task converted; got 0. " +
                $"Check MAIL2PST_CALENDAR_CORPUS points to a populated store (path={storePath}).");

            // Every warning must match a known structured pattern (no raw exception traces).
            foreach (var w in report.Warnings)
            {
                bool recognised = StructuredWarningPatterns.Any(rx => rx.IsMatch(w.Reason));
                Assert.True(recognised,
                    $"Unrecognised (possibly unstructured) warning — check if a new code path " +
                    $"needs a pattern added to StructuredWarningPatterns:\n  {w.Reason}");
            }
        }
        finally { Directory.Delete(outDir, recursive: true); }
    }
}

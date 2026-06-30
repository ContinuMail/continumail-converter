// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Config;
using Microsoft.Data.Sqlite;
using Mail2Pst.Core.Contacts;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Parsing;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;

namespace Mail2Pst.Integration.Tests;

/// <summary>Shared helpers for round-trip tests. Single output group assumed (all fixtures/corpora use one).</summary>
public static class RoundTripHarness
{
    public static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static (IReadOnlyList<string> outputs, ConversionReport report) Convert(ConversionConfig config, string outDir)
    {
        var report = new ConversionRunner().Run(config, outDir);
        return (report.OutputFiles, report);
    }

    /// <summary>
    /// Drives <see cref="PstWriter"/> directly from a list of <see cref="MailMessage"/>s,
    /// placing all messages into a single "Inbox" folder. Used for tests that build messages
    /// in-memory rather than parsing an mbox file.
    /// </summary>
    public static IReadOnlyList<string> ConvertMessages(IReadOnlyList<MailMessage> messages, string outDir)
    {
        var plan = new PstOutputPlan { Name = "P", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = true };
        IEnumerable<PlannedMessage> planned = messages.Select(m => new PlannedMessage
        {
            Message = m,
            TargetFolderPath = new[] { "Inbox" },
        });
        var report = new ConversionReport();
        return new PstWriter().WritePlan(plan, planned, outDir, report);
    }

    /// <summary>
    /// Folder path key -> the successfully-parsed messages planned into it (the writer's input).
    /// Mirrors ConversionRunner's successful-parse planning: same MappingEngine plan and target folder paths.
    /// (It does NOT mirror the runner's mid-stream IOException/UnauthorizedAccessException source-skip path;
    /// Tier B asserts report.Skipped == 0 before calling this, and fixtures are always readable.)
    /// Includes successful ParseResults; unlike the runner (which skips Failed results), this
    /// THROWS on a parse failure so a broken fixture surfaces as a test failure rather than a
    /// silent miscompare. If ConversionRunner ever gains additional skip/filter logic beyond
    /// ParseResult.Success, this must mirror it or the comparer will report false mismatches.
    /// Single output group only (all fixtures and corpus configs use one).
    /// </summary>
    public static Dictionary<string, List<MailMessage>> BuildTruth(ConversionConfig config)
    {
        if (config.Outputs.Count != 1)
            throw new InvalidOperationException(
                $"RoundTripHarness assumes a single output group; config has {config.Outputs.Count}.");

        var truth = new Dictionary<string, List<MailMessage>>(StringComparer.Ordinal);

        // Create a key for the path and EVERY ancestor prefix (ancestors are empty placeholder
        // lists, matching the structural parent folders the writer creates).
        void EnsurePrefixes(IReadOnlyList<string> path)
        {
            for (int depth = 1; depth <= path.Count; depth++)
            {
                string k = FolderPathKey.Join(path.Take(depth).ToArray());
                if (!truth.ContainsKey(k))
                    truth[k] = new List<MailMessage>();
            }
        }

        foreach (PstOutputPlan plan in MappingEngine.BuildPlan(config))
        {
            foreach (SourceMapping mapping in plan.SourceMappings)
            {
                IReadOnlyList<string> path = mapping.TargetFolderPath;

                var msgs = new List<MailMessage>();
                IMailSourceParser parser = ParserRegistry.Get(mapping.Source.Type);
                foreach (ParseResult result in parser.Parse(mapping.Source.Path))
                {
                    if (result.Success)
                        msgs.Add(result.Message!);
                    else
                        // A parse FAILURE in a round-trip source means a broken fixture/parser —
                        // make it loud, never silently exclude it. (A dropped attachment is a
                        // successful parse with a warning, so the broken-attachment fixture is
                        // unaffected.) Tier B asserts report.Skipped == 0 before this runs.
                        throw new InvalidOperationException(
                            $"Parse failure while building truth for '{mapping.Source.Path}': {result.Error}");
                }

                // Mirror the writer EXACTLY:
                //  - >=1 message: create leaf (+ ancestors) and attach messages to the leaf
                //  - 0 messages: create leaf (+ ancestors) ONLY when IncludeEmptyFolders is true
                //  - else: this source contributes no folder
                if (msgs.Count > 0)
                {
                    EnsurePrefixes(path);
                    truth[FolderPathKey.Join(path)].AddRange(msgs);
                }
                else if (plan.IncludeEmptyFolders)
                {
                    EnsurePrefixes(path);
                }
            }

            // Count contacts analogously.
            // The writer ALWAYS pre-creates every contact folder in Begin() (unlike mail folders,
            // which respect IncludeEmptyFolders). Mirror that: always EnsurePrefixes for every
            // ContactMapping, then add one placeholder MailMessage per successfully-read contact
            // (placeholder only — callers that compare message content do so for mail folders;
            // all existing test configs have zero ContactMappings and are unaffected).
            // A read failure throws loud (never silently excluded), matching the mail-failure policy.
            foreach (ContactMapping cm in plan.ContactMappings)
            {
                IReadOnlyList<string> contactPath = cm.TargetFolderPath;
                IAddressBookReader reader = cm.Format == AddressBookFormat.ThunderbirdMab
                    ? (IAddressBookReader)new MorkAddressBookReader()
                    : new SqliteAddressBookReader();
                var book = new AddressBook
                {
                    DisplayName = contactPath[^1],
                    Path = cm.Source.Path,
                    Format = cm.Format,
                };

                // Always pre-create: the writer creates the folder regardless of whether it is empty.
                EnsurePrefixes(contactPath);
                string leafKey = FolderPathKey.Join(contactPath);

                foreach (ContactReadResult r in reader.Read(book))
                {
                    if (r.Success)
                        // Placeholder MailMessage: only the Count is compared by the validation gate.
                        truth[leafKey].Add(new MailMessage());
                    else
                        throw new InvalidOperationException(
                            $"Contact read failure while building truth for '{cm.Source.Path}': {r.Error}");
                }
            }

            // Count tasks analogously.
            // The writer ALWAYS pre-creates every task folder in Begin() (same as contacts),
            // so an empty calendar still yields an IPF.Task folder. Mirror that: always
            // EnsurePrefixes for every TaskMapping, then add one placeholder MailMessage per
            // task that CalendarTaskMapper.Map would actually write (non-null result only —
            // so recurring/skipped todos don't inflate the expected count).
            foreach (TaskMapping tm in plan.TaskMappings)
            {
                IReadOnlyList<string> taskPath = tm.TargetFolderPath;
                // Always pre-create the folder (mirrors runner's Begin() which pre-creates task folders).
                EnsurePrefixes(taskPath);
                string taskLeafKey = FolderPathKey.Join(taskPath);

                // Mirror ConversionRunner.ReadStore: an unreadable store warns + continues with 0 tasks.
                // BuildTruth mirrors that by catching the same exception types and yielding count 0.
                CalendarReadResult calRead;
                try { calRead = new SqliteCalendarReader().Read(tm.Source.StorePath); }
                catch (Exception ex) when (ex is IOException or SqliteException) { continue; }

                IEnumerable<RawCalendarRead> cals = string.IsNullOrEmpty(tm.Source.CalId)
                    ? calRead.Calendars
                    : calRead.Calendars.Where(c => c.CalId == tm.Source.CalId);

                foreach (RawCalendarRead cal in cals)
                {
                    foreach (RawTodoGroup group in cal.TodoGroups)
                    {
                        TaskRecord? mapped = CalendarTaskMapper.Map(group, out _);
                        if (mapped is not null)
                            truth[taskLeafKey].Add(new MailMessage());
                    }
                }
            }

            // Count appointments analogously.
            // The writer ALWAYS pre-creates every appointment folder in Begin() (same as contacts/tasks),
            // so an empty calendar still yields an IPM.Appointment folder. Mirror that: always
            // EnsurePrefixes for every AppointmentMapping, then add one placeholder MailMessage per
            // appointment that CalendarEventMapper.Map would actually write (non-null result only —
            // so recurring/skipped events don't inflate the expected count).
            foreach (AppointmentMapping am in plan.AppointmentMappings)
            {
                IReadOnlyList<string> apptPath = am.TargetFolderPath;
                // Always pre-create the folder (mirrors runner's Begin() which pre-creates appointment folders).
                EnsurePrefixes(apptPath);
                string apptLeafKey = FolderPathKey.Join(apptPath);

                // Mirror ConversionRunner.ReadStore: an unreadable store warns + continues with 0 appointments.
                // BuildTruth mirrors that by catching the same exception types and yielding count 0.
                CalendarReadResult calRead;
                try { calRead = new SqliteCalendarReader().Read(am.Source.StorePath); }
                catch (Exception ex) when (ex is IOException or SqliteException) { continue; }

                IEnumerable<RawCalendarRead> apptCals = string.IsNullOrEmpty(am.Source.CalId)
                    ? calRead.Calendars
                    : calRead.Calendars.Where(c => c.CalId == am.Source.CalId);

                foreach (RawCalendarRead cal in apptCals)
                {
                    foreach (RawEventGroup group in cal.EventGroups)
                    {
                        AppointmentRecord? mapped = CalendarEventMapper.Map(group, out _);
                        if (mapped is not null)
                            truth[apptLeafKey].Add(new MailMessage());
                    }
                }
            }
        }
        return truth;
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Cli;
using Mail2Pst.Core.Discovery;
using Mail2Pst.Core.Msf;

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
                    accountId = s.AccountId,
                }),
                accounts = r.Accounts.Select(a => new
                {
                    id = a.Id, folderSegment = a.FolderSegment, accountPath = a.AccountPath, store = a.Store,
                    email = a.Email, host = a.Host, addressResolution = ResolutionString(a.AddressResolution),
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
                calendars = r.Calendars.Select(c => new
                {
                    calId = c.CalId, displayName = c.DisplayName, storeKind = c.StoreKind,
                    storePath = c.StorePath, calendarType = c.CalendarType,
                    isVisibleInThunderbird = c.IsVisibleInThunderbird,
                    eventCount = c.EventCount, taskCount = c.TaskCount,
                    defaultCalendarFolderPath = c.DefaultCalendarFolderPath,
                    defaultTaskFolderPath = c.DefaultTaskFolderPath,
                }),
                addressBooks = r.AddressBooks.Select(b => new
                {
                    displayName = b.DisplayName, path = b.Path, format = b.Format,
                    contactCount = b.ContactCount, // int? -> JSON number or null
                }),
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

    private static string ResolutionString(AddressResolution r) => r switch
    {
        AddressResolution.Identity => "identity",
        AddressResolution.Server => "server",
        AddressResolution.LocalFolders => "local-folders",
        _ => "not-found",
    };
}

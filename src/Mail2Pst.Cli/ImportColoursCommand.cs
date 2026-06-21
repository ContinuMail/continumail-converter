// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mail2Pst.Core.Cli;
using Mail2Pst.Core.Msf;
using Mail2Pst.Core.OutlookCategories;

namespace Mail2Pst.Cli;

internal static class ImportColoursCommand
{
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(60);

    internal static int Run(string[] args)
    {
        ImportColoursInput input = ImportColoursInput.Parse(args);
        if (input.Error is not null)
        {
            Console.Error.WriteLine($"{input.Error}");
            Console.Error.WriteLine("Usage: continumail-convert import-colours --profile <thunderbird-profile-dir> [--apply]");
            return 1;
        }

        string prefsPath = Path.Combine(input.ProfilePath!, "prefs.js");
        string content = File.Exists(prefsPath) ? File.ReadAllText(prefsPath) : string.Empty;
        IReadOnlyList<CategoryCandidate> plan =
            CategoryColorPlan.Build(PrefsTagReader.ParseText(content), PrefsTagReader.ParseColors(content));

        if (!input.Apply)
        {
            Emit("preview", outlookAvailable: ProgIdRegistered(), plan);
            return 0;
        }

        if (!OperatingSystem.IsWindows() || !ProgIdRegistered())
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours",
                message = "Outlook is required for --apply; preview works without it.", fatal = true });
            Console.Error.WriteLine("Outlook is required for --apply.");
            return 1;
        }

        try
        {
            IReadOnlyList<CategoryCandidate> applied = RunOnSta(() =>
            {
                using var store = new OutlookComCategoryStore();
                return CategoryColorApplier.Apply(plan, store);
            }, ApplyTimeout);
            Emit("apply", outlookAvailable: true, applied);
            return 0;
        }
        catch (TimeoutException)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours",
                message = "Outlook did not respond (a security prompt may be open). Dismiss it / use a trusted context and re-run.",
                fatal = true });
            Console.Error.WriteLine("Outlook timed out — dismiss any Outlook security prompt and re-run.");
            return 1;
        }
        catch (Exception ex)
        {
            CliArgs.WriteJsonLine(new { type = "error", stage = "import-colours", message = ex.Message, fatal = true });
            Console.Error.WriteLine($"import-colours failed: {ex.Message}");
            return 1;
        }
    }

    private static bool ProgIdRegistered() =>
        OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Outlook.Application") is not null;

    private static void Emit(string mode, bool outlookAvailable, IReadOnlyList<CategoryCandidate> categories)
    {
        // NOTE: do NOT set schemaVersion here — CliEventSerializer.Serialize injects it (verified: it does
        // `node["schemaVersion"] = SchemaVersion`). Matches DiscoverCommand, which also omits it.
        var output = new
        {
            type = "importColours",
            mode,
            outlookAvailable,
            categories = categories.Select(c => new { name = c.Name, hex = c.Hex, outlookColor = c.OutlookColor, action = c.Action }),
        };
        Console.WriteLine(CliEventSerializer.Serialize(output, indented: true));
    }

    // Runs the COM interaction on a dedicated STA thread; throws TimeoutException if it doesn't finish in time.
    private static T RunOnSta<T>(Func<T> work, TimeSpan timeout)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout)) throw new TimeoutException();
        if (error is not null) throw error;
        return result;
    }
}

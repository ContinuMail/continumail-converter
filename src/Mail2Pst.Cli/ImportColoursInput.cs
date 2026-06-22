// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Cli;

internal sealed record ImportColoursInput(string? ProfilePath, bool Apply, string? Error, string? PlanFile = null)
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { "--profile", "--apply", "--plan-file" };

    internal static ImportColoursInput Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (!Known.Contains(a)) return new ImportColoursInput(null, false, $"Unknown argument: {a}");
            if (a == "--profile" || a == "--plan-file")
            {
                i++;
                if (i >= args.Length || args[i].StartsWith("--", System.StringComparison.Ordinal))
                    return new ImportColoursInput(null, false, $"{a} requires a value.");
            }
        }
        string? profile = CliArgs.Flag(args, "--profile");
        string? planFile = CliArgs.Flag(args, "--plan-file");
        bool apply = CliArgs.HasFlag(args, "--apply");

        if (profile is not null && planFile is not null)
            return new ImportColoursInput(null, false, "--profile and --plan-file are mutually exclusive; specify exactly one.");

        if (profile is null && planFile is null)
            return new ImportColoursInput(null, false, "Missing required --profile <thunderbird-profile-dir> or --plan-file <path>.");

        return new ImportColoursInput(profile, apply, null, planFile);
    }
}

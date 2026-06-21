// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Cli;

internal sealed record ImportColoursInput(string? ProfilePath, bool Apply, string? Error)
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { "--profile", "--apply" };

    internal static ImportColoursInput Parse(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (!Known.Contains(a)) return new ImportColoursInput(null, false, $"Unknown argument: {a}");
            if (a == "--profile")
            {
                i++;
                if (i >= args.Length || args[i].StartsWith("--", System.StringComparison.Ordinal))
                    return new ImportColoursInput(null, false, "--profile requires a value.");
            }
        }
        string? profile = CliArgs.Flag(args, "--profile");
        if (string.IsNullOrWhiteSpace(profile))
            return new ImportColoursInput(null, false, "Missing required --profile <thunderbird-profile-dir>.");
        return new ImportColoursInput(profile, CliArgs.HasFlag(args, "--apply"), null);
    }
}

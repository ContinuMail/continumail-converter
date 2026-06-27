// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Discovery;

namespace Mail2Pst.Cli;

/// <summary>Resolved convert inputs, or a user-facing Error string. Pure (no signals/conversion).
/// <paramref name="ExpectedTotal"/> is an optional precomputed message count (-1 = count on demand).</summary>
internal sealed record ConvertResolution(ConversionConfig? Config, string? OutputDir, string? InputLabel, string? Error, int ExpectedTotal = -1);

/// <summary>
/// Resolves `convert` arguments into a ConversionConfig (or an error), supporting --config (existing),
/// --profile (discover a Thunderbird profile), and --profile + --config (config = options template).
/// Side-effecting only on the filesystem (reads config / walks profile); no conversion or OS signals,
/// so it is unit-testable (Mail2Pst.Integration.Tests sees it via InternalsVisibleTo).
/// </summary>
internal static class ConvertInput
{
    private static readonly System.Collections.Generic.HashSet<string> KnownFlags =
        new(StringComparer.Ordinal) { "--config", "--profile", "--output", "--expected-total" };

    internal static ConvertResolution Resolve(string[] args)
    {
        // Reject unknown options and stray positional arguments (spec §5.1) before interpreting flags.
        string? argError = ValidateKnownArgs(args);
        if (argError is not null)
            return new(null, null, null, argError);

        string? configPath = CliArgs.Flag(args, "--config");
        string? profileDir = CliArgs.Flag(args, "--profile");
        string? outputDir = CliArgs.Flag(args, "--output");

        // Optional precomputed message count (non-negative). Lets a caller that already scanned skip the
        // convert-time count pass; omitted -> -1 -> count on demand. Direct CLI users are unaffected.
        int expectedTotal = -1;
        string? expectedTotalRaw = CliArgs.Flag(args, "--expected-total");
        if (expectedTotalRaw is not null &&
            (!int.TryParse(expectedTotalRaw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out expectedTotal) || expectedTotal < 0))
        {
            return new(null, null, null, $"Invalid --expected-total value: {expectedTotalRaw}");
        }

        if (outputDir is null)
            return new(null, null, null, "Missing --output <dir>.");
        if (configPath is null && profileDir is null)
            return new(null, null, null, "Provide --config <config.json> or --profile <dir>.");

        if (profileDir is not null)
        {
            if (!Directory.Exists(profileDir))
                return new(null, null, null, $"Profile directory not found: {profileDir}");

            ConversionConfig? template = null;
            if (configPath is not null)
            {
                if (!File.Exists(configPath))
                    return new(null, null, null, $"Config not found: {configPath}");
                try { template = ConfigLoader.Load(configPath); }
                catch (Exception ex) { return new(null, null, null, $"Failed to load config: {ex.Message}"); }
            }

            try
            {
                DiscoveryResult discovery = MailProfileDiscovery.Discover(profileDir);
                ConversionConfig config = ConfigFromDiscovery.Build(discovery, template);
                return new(config, outputDir, profileDir, null, expectedTotal);
            }
            catch (Exception ex)
            {
                return new(null, null, null, ex.Message);
            }
        }

        // --config only (existing behaviour).
        if (!File.Exists(configPath!))
            return new(null, null, null, $"Config not found: {configPath}");
        try
        {
            ConversionConfig config = ConfigLoader.Load(configPath!);
            return new(config, outputDir, configPath, null, expectedTotal);
        }
        catch (Exception ex)
        {
            return new(null, null, null, $"Failed to load config: {ex.Message}");
        }
    }

    // Every arg must be a known `--flag` immediately followed by its value. Rejects unknown options,
    // stray positionals, and a flag whose value is missing (next token is another flag or end-of-args).
    private static string? ValidateKnownArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                return $"Unexpected argument: {arg}";
            if (!KnownFlags.Contains(arg))
                return $"Unknown option: {arg}";
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return $"Missing value for {arg}.";
            i++; // consume the value
        }
        return null;
    }
}

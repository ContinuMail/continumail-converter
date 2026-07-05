// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using Mail2Pst.Core.Cli;

namespace Mail2Pst.Cli;

/// <summary>`outlook-profiles list|create|open` — enumerates the classic-Outlook profile registry,
/// creates a mail-less `/PIM` viewing profile, or launches Outlook into an existing profile.
/// No `--mount` here: creating a profile never touches COM or the converted PST — the user adds
/// that manually afterwards (see OutlookProfileCreator).</summary>
internal static class OutlookProfilesCommand
{
    private const string Usage =
        "Usage: continumail-convert outlook-profiles list\n" +
        "       continumail-convert outlook-profiles create --name <profile-name>\n" +
        "       continumail-convert outlook-profiles open   --name <profile-name>";

    internal static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        string sub = args[0];
        string[] rest = args[1..];
        return sub switch
        {
            "list" => RunList(),
            "create" => RunCreate(rest),
            "open" => RunOpen(rest),
            _ => Unknown(sub),
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown outlook-profiles subcommand '{sub}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    private static int RunList()
    {
        bool classicOutlook = OutlookDetection.ClassicOutlookAvailable();
        OutlookProfileInfo info = OutlookProfileRegistry.Read(new WindowsRegistryKeyReader());
        var output = new
        {
            type = "outlookProfiles",
            classicOutlook,
            defaultProfile = info.DefaultProfile,
            profiles = info.Profiles,
        };
        Console.WriteLine(CliEventSerializer.Serialize(output, indented: true));
        return 0;
    }

    private static int RunCreate(string[] args)
    {
        string? name = ParseName(args, out string? argError);
        if (argError is not null)
        {
            Console.Error.WriteLine(argError);
            Console.Error.WriteLine("Usage: continumail-convert outlook-profiles create --name <profile-name>");
            return 1;
        }
        if (name is null)
        {
            Console.Error.WriteLine("Usage: continumail-convert outlook-profiles create --name <profile-name>");
            return 1;
        }

        try
        {
            (bool created, bool reused) = OutlookProfileCreator.EnsureProfile(name, new WindowsRegistryKeyReader());
            Console.WriteLine(CliEventSerializer.Serialize(
                new { type = "outlookProfileCreate", name, created, reused }, indented: true));
            return 0;
        }
        catch (Exception ex)
        {
            (string stage, string message) = StageFromException(ex);
            CliArgs.WriteJsonLine(new { type = "error", stage, message, fatal = true });
            Console.Error.WriteLine($"outlook-profiles create failed: {message}");
            return 1;
        }
    }

    private static int RunOpen(string[] args)
    {
        string? name = ParseName(args, out string? argError);
        if (argError is not null)
        {
            Console.Error.WriteLine(argError);
            Console.Error.WriteLine("Usage: continumail-convert outlook-profiles open --name <profile-name>");
            return 1;
        }
        if (name is null)
        {
            Console.Error.WriteLine("Usage: continumail-convert outlook-profiles open --name <profile-name>");
            return 1;
        }

        OutlookProfileInfo info = OutlookProfileRegistry.Read(new WindowsRegistryKeyReader());
        ProfileResolution target = OutlookProfileResolver.Resolve(name, info);
        if (target.Name is null)
        {
            string msg = $"Outlook profile '{name}' not found. Available: {string.Join(", ", info.Profiles)}.";
            CliArgs.WriteJsonLine(new { type = "error", stage = target.ErrorCode, message = msg, fatal = true });
            Console.Error.WriteLine(msg);
            return 1;
        }

        try
        {
            // Open does NOT refuse a running Outlook — launching into a profile is the user's explicit ask.
            OutlookProfileCreator.OpenInProfile(target.Name);
            Console.WriteLine(CliEventSerializer.Serialize(
                new { type = "outlookProfileOpen", name = target.Name, launched = true }, indented: true));
            return 0;
        }
        catch (Exception ex)
        {
            (string stage, string message) = StageFromException(ex);
            CliArgs.WriteJsonLine(new { type = "error", stage, message, fatal = true });
            Console.Error.WriteLine($"outlook-profiles open failed: {message}");
            return 1;
        }
    }

    // Only --name is a known flag for create/open — there is NO --mount flag (auto-mount was
    // dropped from scope). Any other argument is rejected rather than silently ignored.
    private static string? ParseName(string[] args, out string? error)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "--name")
            {
                error = $"Unknown argument: {args[i]}";
                return null;
            }
            i++;
            if (i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
            {
                error = "--name requires a value.";
                return null;
            }
        }
        error = null;
        return CliArgs.Flag(args, "--name");
    }

    // [R3:6] Both create and open route exception mapping through here — no per-subcommand
    // re-parsing of the same prefixes.
    private static (string Stage, string Message) StageFromException(Exception ex)
    {
        string msg = ex.Message;
        foreach (string prefix in new[] { "pim-unsupported:", "outlook-spawn-failed:", "invalid-profile-name:" })
        {
            if (msg.StartsWith(prefix, StringComparison.Ordinal))
                return (prefix[..^1], msg[prefix.Length..].Trim());
        }
        return ("outlook-profiles", msg);
    }
}

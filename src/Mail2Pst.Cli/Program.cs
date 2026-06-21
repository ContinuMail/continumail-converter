// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Reflection;
using Mail2Pst.Cli;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  continumail-convert convert  --config <config.json> --output <dir>");
    Console.Error.WriteLine("  continumail-convert convert  --profile <dir> [--config <options.json>] --output <dir>");
    Console.Error.WriteLine("  continumail-convert scan     --input <path> [--input <path> ...] [--type mbox]");
    Console.Error.WriteLine("  continumail-convert discover --input <dir>");
    return 1;
}

return args[0] switch
{
    "convert"            => ConvertCommand.Run(args[1..]),
    "scan"               => ScanCommand.Run(args[1..]),
    "discover"           => DiscoverCommand.Run(args[1..]),
    "version" or "--version" or "-v" => PrintVersion(),
    _                    => PrintUnknownCommand(args[0]),
};

static int PrintVersion()
{
    var asm = Assembly.GetExecutingAssembly();
    string version =
        asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? asm.GetName().Version?.ToString() ?? "0.0.0";
    int plus = version.IndexOf('+'); // strip build metadata like "0.1.0+abc123"
    if (plus >= 0) version = version[..plus];
    CliArgs.WriteJsonLine(new { type = "version", version, engine = "Mail2Pst.Cli" });
    return 0;
}

static int PrintUnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Unknown command '{cmd}'. Use 'convert', 'scan', or 'discover'.");
    return 1;
}

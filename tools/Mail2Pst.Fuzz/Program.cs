// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using System.Linq;
using Mail2Pst.Fuzz;
using SharpFuzz;

// Modes:
//   mork | mbox                 -> libFuzzer-driven fuzzing (requires the instrumented assembly)
//   mork-replay <dir> | mbox-replay <dir>
//                               -> deterministic replay of every file in <dir> through the target,
//                                  no libFuzzer. Exit 0 iff no unexpected exception escaped.
//
// NOTE: RunMork/RunMbox take ReadOnlySpan<byte> (a ref struct), so they CANNOT be stored in an
// Action<ReadOnlySpan<byte>>. Dispatch directly instead. For libFuzzer mode, pass the method group
// straight to Fuzzer.LibFuzzer.Run (SharpFuzz's own delegate type accepts a ReadOnlySpan<byte>).
if (args.Length == 0)
{
    Console.Error.WriteLine("usage: mail2pst-fuzz <mork|mbox|mork-replay <dir>|mbox-replay <dir>>");
    return 2;
}

// A byte[] argument binds implicitly to the ReadOnlySpan<byte> parameter, so replay can dispatch here.
static void RunTarget(string mode, byte[] bytes)
{
    if (mode.StartsWith("mork", StringComparison.Ordinal))
        FuzzTargets.RunMork(bytes);
    else if (mode.StartsWith("mbox", StringComparison.Ordinal))
        FuzzTargets.RunMbox(bytes);
    else
        throw new ArgumentException($"unknown mode '{mode}'");
}

if (args[0].EndsWith("-replay", StringComparison.Ordinal))
{
    if (args.Length < 2) { Console.Error.WriteLine("replay needs a corpus directory"); return 2; }
    int n = 0, unexpected = 0;
    // Sort for deterministic output across OSes/filesystems.
    foreach (string file in Directory.EnumerateFiles(args[1]).OrderBy(p => p, StringComparer.Ordinal))
    {
        n++;
        try { RunTarget(args[0], File.ReadAllBytes(file)); }
        catch (Exception ex)
        {
            unexpected++;
            Console.Error.WriteLine($"UNEXPECTED {ex.GetType().Name} on {Path.GetFileName(file)}: {ex.Message}");
        }
    }
    Console.WriteLine($"{n} inputs, {unexpected} unexpected exceptions");
    return unexpected == 0 ? 0 : 1;
}

// libFuzzer mode: pass the method group directly (verify the exact delegate signature against the
// installed SharpFuzz version when compiling — current docs use Fuzzer.LibFuzzer.Run).
switch (args[0])
{
    case "mork": Fuzzer.LibFuzzer.Run(FuzzTargets.RunMork); return 0;
    case "mbox": Fuzzer.LibFuzzer.Run(FuzzTargets.RunMbox); return 0;
    default:
        Console.Error.WriteLine($"unknown mode '{args[0]}'");
        return 2;
}

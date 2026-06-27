// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using Xunit;

namespace Mail2Pst.Core.Tests.Cli;

/// <summary>Shared helpers for end-to-end tests that spawn the real built CLI
/// (`dotnet Mail2Pst.Cli.dll …`). Locating the repo root + the build output is
/// identical across E2E suites; keeping it in one place avoids drift.</summary>
internal static class CliE2EProcess
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Mail2Pst.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mail2Pst.sln).");
    }

    internal static string CliDllPath()
    {
        string config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/bin/Release/") ? "Release" : "Debug";
        string dll = Path.Combine(RepoRoot(), "src", "Mail2Pst.Cli", "bin", config, "net8.0", "Mail2Pst.Cli.dll");
        Assert.True(File.Exists(dll), $"CLI build output not found at {dll}");
        return dll;
    }
}

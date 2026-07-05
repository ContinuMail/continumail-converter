// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Mail2Pst.Cli;

internal interface IRegistryKeyReader
{
    string[] SubKeyNames(string path);      // empty when the key is absent
    string? StringValue(string path, string name); // null when key/value absent
}

internal sealed class WindowsRegistryKeyReader : IRegistryKeyReader
{
    public string[] SubKeyNames(string path)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<string>();
        using RegistryKey? k = Registry.CurrentUser.OpenSubKey(path);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public string? StringValue(string path, string name)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using RegistryKey? k = Registry.CurrentUser.OpenSubKey(path);
        return k?.GetValue(name) as string;
    }
}

internal sealed record OutlookProfileInfo(IReadOnlyList<string> Profiles, string? DefaultProfile);

/// <summary>Raw Outlook profile facts from the classic-Outlook 16.0 hive (read side proven on the
/// owner machine 2026-07-05; /PIM landing spot pinned by Task 0). Reports facts only — validity
/// decisions live in OutlookProfileResolver.</summary>
internal static class OutlookProfileRegistry
{
    internal const string ProfilesKey = @"Software\Microsoft\Office\16.0\Outlook\Profiles";
    internal const string OutlookKey = @"Software\Microsoft\Office\16.0\Outlook";

    internal static OutlookProfileInfo Read(IRegistryKeyReader reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        string[] profiles = reg.SubKeyNames(ProfilesKey).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        return new OutlookProfileInfo(profiles, reg.StringValue(OutlookKey, "DefaultProfile"));
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Win32;

namespace Mail2Pst.Cli;

/// <summary>Creates a mail-less Outlook profile via `outlook.exe /PIM <name>` (empirically
/// supported switch — NOT on the official switch list; verified on the owner build by Task 0).
/// KB-004 discipline: refuse if Outlook runs, only ever shut down the instance we spawned
/// (CloseMainWindow → bounded wait → kill-only-ours; no COM attachment to a possibly-unrelated
/// Outlook object).</summary>
internal static class OutlookProfileCreator
{
    private const int RegistryWaitMs = 30_000;
    // The profile (and its default store) is already persisted by the time its key appears (~1 s),
    // and a fresh /PIM Outlook typically ignores CloseMainWindow — so a long graceful window is just
    // dead time before the kill. Keep it short: try CloseMainWindow, wait briefly, then kill.
    private const int ShutdownWaitMs = 5_000;

    internal static (bool Created, bool Reused) EnsureProfile(string name, IRegistryKeyReader reg)
    {
        string? nameError = OutlookDetection.ValidateProfileName(name);
        if (nameError is not null) throw new InvalidOperationException($"invalid-profile-name: {nameError}");

        if (OutlookProfileRegistry.Read(reg).Profiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            return (false, true); // idempotent reuse

        if (Process.GetProcessesByName("OUTLOOK").Length != 0)
            throw new InvalidOperationException("Outlook is running. Close Outlook completely, then re-run.");

        string exe = ResolveOutlookExe();
        Process spawned;
        // UseShellExecute=true so the launched Outlook does NOT inherit our stdout/stderr handles.
        // Under the desktop sidecar those handles are the pipe Tauri's run_sidecar_capture reads;
        // an inherited pipe never reaches EOF until Outlook (and any Office background process) dies,
        // so the GUI hangs to the 120 s cap even though the profile was already created. ShellExecute
        // gives Outlook its own handles; CloseMainWindow/WaitForExit/Kill on the returned Process still work.
        try { spawned = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "/PIM", name }, UseShellExecute = true })!; }
        catch (Exception ex) { throw new InvalidOperationException($"outlook-spawn-failed: {ex.Message}"); }

        bool appeared = false;
        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < RegistryWaitMs)
            {
                if (OutlookProfileRegistry.Read(reg).Profiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    appeared = true;
                    break;
                }
                if (spawned.HasExited) break; // Outlook gave up (or /PIM unsupported): stop waiting
                Thread.Sleep(500);
            }
        }
        finally
        {
            // Shut down ONLY the instance we spawned. CloseMainWindow is a no-op (false) while the
            // window isn't up yet; the bounded wait + kill covers that and any hang.
            try { if (!spawned.HasExited) spawned.CloseMainWindow(); } catch { }
            try { if (!spawned.WaitForExit(ShutdownWaitMs)) spawned.Kill(entireProcessTree: true); } catch { }
            spawned.Dispose();
        }

        if (!appeared)
            throw new InvalidOperationException(
                "pim-unsupported: Outlook did not create the profile (the /PIM switch may be unavailable on this build). Create a profile manually in Outlook, then retry.");
        return (true, false);
    }

    internal static string ResolveOutlookExe()
    {
        if (OperatingSystem.IsWindows())
        {
            // [R3:2] App Paths can be registered per-user (HKCU) as well as machine-wide (HKLM) —
            // per-user Office / Click-to-Run installs land under HKCU — so probe HKCU first, then
            // HKLM, then fall back to PATH. (The base keys are static — never dispose them; only the
            // opened subkey is `using`-scoped.)
            const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE";
            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using RegistryKey? k = root.OpenSubKey(appPaths);
                if (k?.GetValue(null) is string path && !string.IsNullOrWhiteSpace(path))
                    return path;
            }
        }
        return "outlook.exe"; // PATH fallback
    }

    internal static void OpenInProfile(string name)
    {
        string? nameError = OutlookDetection.ValidateProfileName(name);
        if (nameError is not null) throw new InvalidOperationException($"invalid-profile-name: {nameError}");
        string exe = ResolveOutlookExe();
        // ARGUMENT ARRAY — profile names may contain spaces; never build a shell string.
        // UseShellExecute=true (see EnsureProfile): otherwise the launched Outlook inherits our stdout
        // pipe and hangs the sidecar-capturing GUI until Outlook is closed / the 120 s cap is hit.
        Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "/profile", name }, UseShellExecute = true })?.Dispose();
    }
}

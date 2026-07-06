// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32;

namespace Mail2Pst.Cli;

/// <summary>Creates a mail-less Outlook profile via `outlook.exe /PIM <name>` (empirically
/// supported switch — NOT on the official switch list; verified on the owner build by Task 0).
/// KB-004 discipline: refuse if Outlook runs, only ever shut down the instance we spawned —
/// via a graceful COM <c>Application.Quit()</c> (see <see cref="ShutdownSpawnedOutlook"/>), falling
/// back to a kill of ONLY the process we started if COM attach/quit doesn't land in time. A hard
/// `Kill(entireProcessTree: true)` corrupts Outlook's navpane/profile state on a live machine
/// (observed: next launch fails with "cannot open the Outlook window") — a clean COM Quit lets
/// Outlook flush and exit without corruption.</summary>
internal static class OutlookProfileCreator
{
    private const int RegistryWaitMs = 30_000;
    // Deadlock cap, not a success timer: wait for clean self-exit (flush) before kill.
    private const int ShutdownWaitMs = 45_000;
    private const int ComReadyWaitMs = 20_000;     // wait for the /PIM Outlook's COM server to become attachable
    private const int GracefulExitWaitMs = 30_000; // wait for Outlook to exit on its own after Quit()

    internal static (bool Created, bool Reused, bool ShutdownClean) EnsureProfile(string name, IRegistryKeyReader reg)
    {
        string? nameError = OutlookDetection.ValidateProfileName(name);
        if (nameError is not null) throw new InvalidOperationException($"invalid-profile-name: {nameError}");

        if (OutlookProfileRegistry.Read(reg).Profiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            return (false, true, true); // idempotent reuse, nothing spawned

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
        bool shutdownClean;
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
            // Shut down ONLY the instance we spawned (KB-004): the "Outlook is running" guard above
            // means the COM instance we attach to below is the one we just launched, never a
            // possibly-unrelated user session.
            shutdownClean = ShutdownSpawnedOutlook(spawned);
            spawned.Dispose();
        }

        if (!appeared)
            throw new InvalidOperationException(
                "pim-unsupported: Outlook did not create the profile (the /PIM switch may be unavailable on this build). Create a profile manually in Outlook, then retry.");
        return (true, false, shutdownClean);
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

    /// <summary>Gracefully closes the /PIM Outlook we spawned via COM <c>Application.Quit()</c> so it
    /// flushes and exits cleanly, instead of a force-kill (which corrupts Outlook's navpane/profile
    /// state — observed on a live machine as "cannot open the Outlook window" on the next launch).
    /// KB-004: we already refused to run if Outlook was running before we spawned (see
    /// <see cref="EnsureProfile"/>), so the COM instance we attach to here is guaranteed to be the one
    /// we started, never a possibly-unrelated user session.
    ///
    /// Sequence: attach via late-bound COM on a dedicated STA thread (retrying up to
    /// <see cref="ComReadyWaitMs"/> while the freshly-spawned process's COM server registers), call
    /// <c>Quit()</c>, then wait up to <see cref="GracefulExitWaitMs"/> for the process to exit on its
    /// own. Only if COM attach/quit doesn't land, or the process doesn't exit afterward, do we fall
    /// back to killing ONLY the process we started (never <c>entireProcessTree: true</c> — killing the
    /// shared Office broker tree is the corruptor we are eliminating).</summary>
    [SupportedOSPlatform("windows")]
    private static bool ShutdownSpawnedOutlook(Process spawned)
    {
        if (spawned.HasExited) return true; // already closed on its own

        bool quitIssued;
        try
        {
            quitIssued = Sta.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < ComReadyWaitMs)
                {
                    if (spawned.HasExited) return true; // gone before we even attached

                    try
                    {
                        Type? t = Type.GetTypeFromProgID("Outlook.Application");
                        object? app = t is not null ? Activator.CreateInstance(t) : null;
                        if (app is not null)
                        {
                            try { ((dynamic)app).Quit(); } catch { /* best-effort */ }
                            try { Marshal.FinalReleaseComObject(app); } catch { /* best-effort */ }
                            return true;
                        }
                    }
                    catch { /* COM server not ready yet — retry */ }

                    Thread.Sleep(500);
                }
                return false; // never managed to attach within the window
            }, TimeSpan.FromMilliseconds(ComReadyWaitMs + 5000));
        }
        catch (TimeoutException)
        {
            quitIssued = false; // STA worker itself hung — treat as "quit not issued"
        }

        if (quitIssued && spawned.WaitForExit(GracefulExitWaitMs))
            return true; // clean, graceful shutdown — no corruption risk

        // Last resort: kill ONLY the window process we started. Deliberately NOT
        // entireProcessTree: true — that would also tear down the shared Office broker process,
        // which is the exact force-kill behavior that corrupts Outlook's navpane/profile state.
        try { if (!spawned.HasExited) spawned.Kill(); } catch { /* best-effort */ }
        try { spawned.WaitForExit(5000); } catch { /* best-effort */ }
        return false;
    }
}

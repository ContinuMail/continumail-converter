// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.IO;
using System.Threading;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Runs a file operation, retrying ONLY on a transient Windows sharing/lock violation
/// (HResult low word 32/33) — the case where an AV scanner briefly holds a just-created
/// or just-closed file. Any other IOException, and any non-IOException, surfaces
/// immediately. On exhaustion the original transient exception propagates (the exception
/// filter leaves it uncaught, so its stack trace is preserved).
/// </summary>
internal static class TransientFileRetry
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    // Backoff (ms) before retries 2..5. Length defines the retry count, so the total
    // attempt count is BackoffMs.Length + 1 = 5.
    private static readonly int[] BackoffMs = { 50, 100, 200, 400 };

    internal static void Run(Action fileOp)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                fileOp();
                return;
            }
            catch (IOException ex) when (IsTransientFileLock(ex) && attempt < BackoffMs.Length)
            {
                Thread.Sleep(BackoffMs[attempt]);
            }
        }
    }

    private static bool IsTransientFileLock(IOException ex)
    {
        // Win32 ERROR_SHARING_VIOLATION (32) / ERROR_LOCK_VIOLATION (33) live in the low word of
        // HResult on Windows only (e.g. an AV scanner holding a freshly-written PST open). On
        // Linux/macOS IOException.HResult carries a different (errno-derived) value, so this never
        // matches and the retry is a deliberate no-op there — that hold-open is a Windows phenomenon.
        int lowWord = ex.HResult & 0xFFFF;
        return lowWord == ErrorSharingViolation || lowWord == ErrorLockViolation;
    }
}

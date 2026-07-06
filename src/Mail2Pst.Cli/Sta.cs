// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Threading;

namespace Mail2Pst.Cli;

/// <summary>Shared STA-thread runner for COM interop work. Extracted from the byte-identical
/// private <c>RunOnSta&lt;T&gt;</c> helpers formerly duplicated in <c>ImportColoursCommand</c>
/// and <c>OutlookCategoryVerifier</c> — pure extraction, no behavior change.</summary>
internal static class Sta
{
    // Runs the COM interaction on a dedicated STA thread; throws TimeoutException if it doesn't finish in time.
    internal static T Run<T>(Func<T> work, TimeSpan timeout)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout)) throw new TimeoutException();
        if (error is not null) throw error;
        return result;
    }
}

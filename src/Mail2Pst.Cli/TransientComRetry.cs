// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;

namespace Mail2Pst.Cli;

internal static class TransientComRetry
{
    internal static T Run<T>(Func<T> open, Func<Exception, bool> isTransient, int maxAttempts, Action<int> sleep)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return open(); }
            catch (Exception ex) when (attempt < maxAttempts && isTransient(ex)) { sleep(attempt); }
        }
    }
}

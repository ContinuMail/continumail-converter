// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class TransientComRetryTests
{
    [Fact]
    public void Run_RetriesTransientThenSucceeds()
    {
        int calls = 0;
        int r = TransientComRetry.Run<int>(
            open: () => { calls++; if (calls < 3) throw new InvalidOperationException("transient"); return 42; },
            isTransient: ex => ex.Message == "transient",
            maxAttempts: 5, sleep: _ => { });
        Assert.Equal(42, r);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Run_NonTransient_RethrowsImmediately()
    {
        int calls = 0;
        Assert.Throws<UnauthorizedAccessException>(() => TransientComRetry.Run<int>(
            open: () => { calls++; throw new UnauthorizedAccessException(); },
            isTransient: _ => false, maxAttempts: 5, sleep: _ => { }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Run_TransientExhausted_Rethrows()
    {
        Assert.Throws<InvalidOperationException>(() => TransientComRetry.Run<int>(
            open: () => throw new InvalidOperationException("transient"),
            isTransient: _ => true, maxAttempts: 2, sleep: _ => { }));
    }
}

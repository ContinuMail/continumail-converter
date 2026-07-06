// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class ColourApplyCoordinatorTests
{
    private static ColourApplyResult Run(Queue<bool> attempts, Queue<bool> verifies, List<string> log)
        => ColourApplyCoordinator.Run(
            attempt: () => { log.Add("attempt"); return attempts.Dequeue(); },
            coldVerify: () => { log.Add("verify"); return verifies.Dequeue(); },
            ensureNoTrackedProcess: () => log.Add("guard"));

    [Fact]
    public void CleanExit_Succeeds_NoColdVerify()
    {
        var log = new List<string>();
        var r = Run(new Queue<bool>(new[] { true }), new Queue<bool>(), log);
        Assert.True(r.Success);
        Assert.True(r.ShutdownClean);
        Assert.False(r.ColdVerifyAttempted);
        Assert.False(r.ColdVerified);
        Assert.Equal(0, r.RetryCount);
        Assert.DoesNotContain("verify", log);
        Assert.Equal("guard", log[0]); // guard runs before the first attempt
    }

    [Fact]
    public void Kill_ThenColdVerifyPasses_Succeeds_NoRetry()
    {
        var log = new List<string>();
        var r = Run(new Queue<bool>(new[] { false }), new Queue<bool>(new[] { true }), log);
        Assert.True(r.Success);
        Assert.False(r.ShutdownClean);
        Assert.True(r.ColdVerifyAttempted);
        Assert.True(r.ColdVerified);
        Assert.Equal(0, r.RetryCount);
    }

    [Fact]
    public void Kill_VerifyMiss_RetryCleanExit_Succeeds_NoSecondVerify()
    {
        var log = new List<string>();
        var r = Run(new Queue<bool>(new[] { false, true }), new Queue<bool>(new[] { false }), log);
        Assert.True(r.Success);
        Assert.True(r.ShutdownClean);          // final attempt exited cleanly — not overloaded by the earlier kill
        Assert.True(r.ColdVerifyAttempted);    // a cold verify did run (and missed) before the clean retry
        Assert.False(r.ColdVerified);          // final success did NOT depend on cold verify
        Assert.Equal(1, r.RetryCount);
        Assert.Single(log.FindAll(s => s == "verify")); // only the first (pre-retry) cold verify
    }

    [Fact]
    public void Kill_VerifyMiss_RetryKill_VerifyMiss_Fails()
    {
        var log = new List<string>();
        var r = Run(new Queue<bool>(new[] { false, false }), new Queue<bool>(new[] { false, false }), log);
        Assert.False(r.Success);
        Assert.Equal("colour-apply-unverified", r.FailureStage);
        Assert.Equal(1, r.RetryCount);
    }

    [Fact]
    public void AttemptThrows_Propagates()
    {
        Assert.Throws<InvalidOperationException>(() => ColourApplyCoordinator.Run(
            attempt: () => throw new InvalidOperationException("readback"),
            coldVerify: () => true,
            ensureNoTrackedProcess: () => { }));
    }
}

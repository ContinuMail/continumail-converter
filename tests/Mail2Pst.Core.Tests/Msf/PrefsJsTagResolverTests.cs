// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Msf;
using Xunit;

namespace Mail2Pst.Core.Tests.Msf;

public class PrefsJsTagResolverTests
{
    private static PrefsJsTagResolver Resolver(params (string key, string name)[] prefs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, n) in prefs) map[k] = n;
        return new PrefsJsTagResolver(map);
    }

    [Fact]
    public void PrefsOverridesBuiltin()
    {
        var r = Resolver(("$label1", "Critique"));
        Assert.Equal(new[] { "Critique" }, r.Resolve(new[] { "$label1" }));
    }

    [Fact]
    public void BuiltinFallback_WhenPrefsLacksLabel()
    {
        var r = Resolver(); // empty prefs
        Assert.Equal(new[] { "Important" }, r.Resolve(new[] { "$label1" }));
    }

    [Fact]
    public void CustomKey_UsesPrefsName()
    {
        var r = Resolver(("custom", "Client X"));
        Assert.Equal(new[] { "Client X" }, r.Resolve(new[] { "custom" }));
    }

    [Fact]
    public void UnknownKey_PassesThrough()
    {
        var r = Resolver();
        Assert.Equal(new[] { "weirdkey" }, r.Resolve(new[] { "weirdkey" }));
    }

    [Fact]
    public void NonJunk_IsFiltered()
    {
        var r = Resolver(("$label1", "Critique"));
        Assert.Equal(new[] { "Critique" }, r.Resolve(new[] { "NonJunk", "$label1" }));
    }

    [Fact]
    public void Dedupe_PreservesFirstOccurrence_OnCollision()
    {
        // prefs renames $label2 to "Work"; a custom key also named "Work" collides -> first wins, one entry.
        var r = Resolver(("$label2", "Work"), ("dup", "Work"));
        Assert.Equal(new[] { "Work" }, r.Resolve(new[] { "$label2", "dup" }));
    }

    [Fact]
    public void RenamedBuiltin_AndCustom_InOnePass()
    {
        var r = Resolver(("$label1", "Critique"), ("custom", "Client X"));
        Assert.Equal(new[] { "Critique", "Client X" }, r.Resolve(new[] { "$label1", "custom" }));
    }
}

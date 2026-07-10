// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using CsCheck;
using Xunit;

namespace Mail2Pst.Property.Tests;

// Proves the CsCheck dependency resolves and its Sample API works in this project before the real
// generators are built. If the exact Sample/Gen syntax differs in the installed CsCheck version,
// fix it HERE first — the rest of the slice depends on it.
public class SmokeGeneratorTests
{
    [Fact]
    public void CsCheck_Sample_RunsAndVerifiesASimpleProperty()
    {
        Gen.Int[0, 100].Sample(n => Assert.InRange(n, 0, 100));
    }

    // Proves the exact APIs the real generators depend on: the bounded-collection indexer and LINQ
    // query syntax. If .Array[min,max] is spelled differently in the installed CsCheck (e.g. .List),
    // fix it HERE and mirror that choice in ConfigGenerators.
    [Fact]
    public void CsCheck_BoundedArrayAndQuerySyntax_Work()
    {
        Gen<int[]> arr =
            from a in Gen.Int[0, 9].Array[0, 3]
            select a;
        arr.Sample(a => Assert.InRange(a.Length, 0, 3));
    }

    // Proves the remaining API surface the real generator/property use: Gen.OneOfConst, Gen.Bool, and
    // the Sample(iter:, seed:, threads:) named args. If any differ in the installed CsCheck, fix HERE
    // and mirror in ConfigGenerators / the property test.
    [Fact]
    public void CsCheck_OneOfConstBoolAndSampleThreads_Work()
    {
        Gen<long> sizes = Gen.OneOfConst(-5L, 0L, 1L, 51200L, long.MaxValue);
        var gen =
            from b in Gen.Bool
            from n in sizes
            select (b, n);
        gen.Sample(x => Assert.Contains(x.n, new[] { -5L, 0L, 1L, 51200L, long.MaxValue }),
            iter: 10, seed: "00001e2WUFC1", threads: 1);
    }
}

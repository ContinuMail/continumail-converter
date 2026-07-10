// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using CsCheck;

namespace Mail2Pst.Property.Tests;

// Bounded generators for a ConfigRecipe, built with CsCheck LINQ query syntax (SelectMany) so the
// number of fields does not depend on any Gen.Select arity overload. NOTE: verify CsCheck's exact
// combinator API against the installed version and adjust syntax if needed (Gen.Int[min,max] indexer,
// .Array[min,max] indexer, Gen.OneOfConst, query syntax) — the intent (small, bounded, covers valid +
// each invalid variant) is fixed. Records take IReadOnlyList<T>, which SourceRecipe[]/OutputRecipe[]
// satisfy, so Array generators bind directly.
public static class ConfigGenerators
{
    private static readonly Gen<SourceRecipe> Source =
        from tk in Gen.Int[0, 5]
        select new SourceRecipe(tk);

    // MaxSizeMB spans invalid-low, valid, the cap boundary, and Int64-overflow-range values.
    private static readonly Gen<long> MaxSizeMB =
        Gen.OneOfConst(-5L, 0L, 1L, 20000L, 51200L, 51201L, long.MaxValue);

    private static readonly Gen<OutputRecipe> Output =
        from nameKind in Gen.Int[0, 3]
        from size in MaxSizeMB
        from mirror in Gen.Bool
        from includeEmpty in Gen.Bool
        from sourcesKind in Gen.Int[0, 2]
        from sources in Source.Array[0, 2]
        select new OutputRecipe(nameKind, size, mirror, includeEmpty, sourcesKind, sources);

    public static readonly Gen<ConfigRecipe> Config =
        from outs in Output.Array[1, 3]
        select new ConfigRecipe(outs);
}

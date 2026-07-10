// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Mail2Pst.TestSupport;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Path-keyed EXPECTED message counts for a generated profile, derived from the generation truth
/// (what the builder wrote) rather than by re-parsing. Unlike <see cref="RoundTripHarness.BuildTruth"/>
/// (which parses each source and THROWS on a parse failure), this models an intentional skip as
/// "written - skips", so a corruption knob whose message is expected to be dropped can still be
/// checked. Uses <see cref="MappingEngine.BuildPlan"/> for target folder paths, so mirror-mode name
/// sanitization / collision disambiguation match the writer exactly.
/// </summary>
public static class GenerativeTruth
{
    public static Dictionary<string, int> BuildExpected(
        ConversionConfig config,
        GeneratedProfileCounts truth,
        IReadOnlyDictionary<string, int> perSourcePathSkips)
    {
        if (config.Outputs.Count != 1)
            throw new InvalidOperationException($"single output group expected; got {config.Outputs.Count}.");

        var expected = new Dictionary<string, int>(StringComparer.Ordinal);

        void EnsurePrefixes(IReadOnlyList<string> path)
        {
            for (int depth = 1; depth <= path.Count; depth++)
            {
                string k = FolderPathKey.Join(path.Take(depth).ToArray());
                if (!expected.ContainsKey(k)) expected[k] = 0;
            }
        }

        foreach (PstOutputPlan plan in MappingEngine.BuildPlan(config))
        {
            foreach (SourceMapping mapping in plan.SourceMappings)
            {
                int written = truth.CountByPath.TryGetValue(mapping.Source.Path, out int w) ? w : 0;
                int skips = perSourcePathSkips.TryGetValue(mapping.Source.Path, out int s) ? s : 0;
                int count = Math.Max(0, written - skips);

                // Mirror the writer: >=1 msg creates the leaf (+ancestors); 0 msgs creates it only
                // when IncludeEmptyFolders is true.
                if (count > 0)
                {
                    EnsurePrefixes(mapping.TargetFolderPath);
                    expected[FolderPathKey.Join(mapping.TargetFolderPath)] += count;
                }
                else if (plan.IncludeEmptyFolders)
                {
                    EnsurePrefixes(mapping.TargetFolderPath);
                }
            }
        }
        return expected;
    }
}

/// <summary>Generation truth keyed by the source mbox file path.
/// <see cref="FromAttempted"/> counts every message the builder wrote INCLUDING a truncated tail
/// (so the adjusted-truth path subtracts the expected skip from it); <see cref="FromWritten"/> counts
/// only the complete messages (the clean-profile case, where nothing is expected to be skipped).</summary>
public sealed record GeneratedProfileCounts(IReadOnlyDictionary<string, int> CountByPath)
{
    public static GeneratedProfileCounts FromAttempted(GeneratedProfile profile) =>
        new(profile.Folders.ToDictionary(
            f => f.FilePath, f => f.MessageCount + f.TruncatedTailCount, StringComparer.Ordinal));

    public static GeneratedProfileCounts FromWritten(GeneratedProfile profile) =>
        new(profile.Folders.ToDictionary(
            f => f.FilePath, f => f.MessageCount, StringComparer.Ordinal));
}

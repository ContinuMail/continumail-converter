// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsCheck;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Mail2Pst.TestSupport;
using Xunit;

namespace Mail2Pst.Integration.Tests;

[Trait("Category", "GenerativeRoundTrip")]
public class GenerativeRoundTripTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    // Zero-count implementation folders the from-scratch store surfaces (same as the fixed gate).
    private static readonly HashSet<string> ZeroCountAllowlist = new(StringComparer.Ordinal)
    {
        FolderPathKey.Join(new[] { "Deleted Items" }),
        FolderPathKey.Join(new[] { "Calendar" }),
    };

    // OS-creatable, PST-valid, DISTINCT folder-name pool: ASCII, a Danish PT_UNICODE name, spaces.
    // Distinct filenames avoid on-disk collisions in the builder; mirror-mode mapping is applied
    // identically to truth and writer, so any sanitization stays self-consistent.
    private static readonly string[] NamePool =
    {
        "Inbox", "Sent", "Archive 2024", "Projects", "Færdige opgaver", "Kladder", "Rejser", "Kvitteringer",
    };

    private sealed record FolderRecipe(int NameIndex, int MessageCount);

    // 1..5 folders, each with a distinct name (by index) and 0..4 messages.
    private static readonly Gen<FolderRecipe[]> ProfileGen =
        from indices in Gen.Shuffle(Enumerable.Range(0, NamePool.Length).ToArray())
        from n in Gen.Int[1, 5]
        from counts in Gen.Int[0, 4].Array[n, n]
        select indices.Take(n).Zip(counts, (idx, c) => new FolderRecipe(idx, c)).ToArray();

    [SkippableFact]
    [Trait("Tier", "local")]
    public void GeneratedProfiles_RoundTripCountsMatchIndependentReader()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe.");
        Skip.If(Environment.GetEnvironmentVariable("MAIL2PST_ENABLE_GENERATIVE_ROUNDTRIP") is not "1",
            "Set MAIL2PST_ENABLE_GENERATIVE_ROUNDTRIP=1 to run the generative round-trip oracle (local, slow).");

        ProfileGen.Sample(recipe =>
        {
            var builder = new ThunderbirdProfileBuilder().WithAccount("alice@example.com", "imap.example.com");
            foreach (FolderRecipe f in recipe)
                builder.WithFolder(NamePool[f.NameIndex], f.MessageCount);
            using GeneratedProfile profile = builder.Build();

            string outDir = Path.Combine(Path.GetTempPath(), "m2p-genrt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            try
            {
                var config = new ConversionConfig
                {
                    Outputs = new List<OutputGroupConfig>
                    {
                        new()
                        {
                            Name = "Archive",
                            MaxSizeMB = 50_000,
                            FolderMapping = FolderMappingMode.Mirror,
                            IncludeEmptyFolders = true,
                            Sources = profile.Folders
                                .Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath })
                                .ToList(),
                        },
                    },
                };

                var (outputs, report) = RoundTripHarness.Convert(config, outDir);
                Assert.NotEmpty(outputs);
                Assert.Empty(report.Skipped); // clean profile: nothing skipped

                // Expected (clean): known WRITTEN counts, no skips. Cross-check it agrees with BuildTruth.
                Dictionary<string, int> expected = GenerativeTruth.BuildExpected(
                    config, GeneratedProfileCounts.FromWritten(profile), new Dictionary<string, int>());
                Dictionary<string, int> byParse = RoundTripHarness.BuildTruth(config)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);
                Assert.Equal(byParse.OrderBy(k => k.Key), expected.OrderBy(k => k.Key)); // truth models agree

                AssertIndependentCountsMatch(outputs, expected);
            }
            finally { Directory.Delete(outDir, true); }
        }, iter: 25, seed: "00001e2WUFC1", threads: 1);
    }

    // Verification lock: determine whether the PRODUCTION convert path skips a truncated final message.
    // A truncated tail after N complete messages yields EITHER N (MimeKit parsed the fragment; no skip)
    // OR N + a recorded skip (fragment rejected). Whichever it is, this pins it so GenerativeTruth's
    // expected-skip count is correct. Runs without the Rust reader (asserts on the ConversionReport only).
    [Fact]
    [Trait("Tier", "local")]
    public void TruncatedFinalMessage_ConvertBehavior_IsPinned()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithTruncatedFinalMessage("Inbox", fullMessageCount: 2)
            .Build();

        string outDir = Path.Combine(Path.GetTempPath(), "m2p-trunc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var config = new ConversionConfig
            {
                Outputs = new List<OutputGroupConfig>
                {
                    new()
                    {
                        Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror,
                        IncludeEmptyFolders = true,
                        Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
                    },
                },
            };
            var (_, report) = RoundTripHarness.Convert(config, outDir);

            // EMPIRICALLY PINNED (Slice 3b Task 2, 2026-07-10): 2 complete messages + 1 truncated tail
            // were written; the PRODUCTION convert path parsed all 3 (MimeKit's entity parser accepts
            // the truncated fragment as a malformed-but-parseable final message) — ConvertedCount == 3,
            // Skipped.Count == 0, no skip recorded. Per-message mid-body truncation is therefore NOT a
            // reliable convert-level skip signal; the reliable skip paths remain oversized-message
            // (parser-level, Slice-1 coverage) and whole-source IOException. Per the Slice 3b Task 2
            // brief's verify gate, this PARSED outcome means the adjusted-truth corruption case (Step 4)
            // is deliberately NOT wired into the generative oracle for this shape — the generative clean
            // round-trip (Task 1) remains the delivered oracle.
            Assert.Equal(3, report.ConvertedCount);
            Assert.Empty(report.Skipped);
        }
        finally { Directory.Delete(outDir, true); }
    }

    // Pure unit test of GenerativeTruth's adjusted-truth arithmetic (written - skips). No conversion or
    // Rust reader — runs in normal CI. Covers GeneratedProfileCounts.FromAttempted + the non-empty
    // perSourcePathSkips path, which the PARSED verify-gate outcome otherwise leaves unexercised.
    [Fact]
    public void GenerativeTruth_BuildExpected_SubtractsExpectedSkips()
    {
        // Mirror mode maps the source file to a folder named by its filename stem ("Inbox").
        // BuildPlan does NOT read the file, so a non-existent path is fine for this pure test.
        string src = Path.Combine(Path.GetTempPath(), "Inbox");
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Archive",
                    MaxSizeMB = 50_000,
                    FolderMapping = FolderMappingMode.Mirror,
                    IncludeEmptyFolders = true,
                    Sources = new List<SourceConfig> { new() { Type = "mbox", Path = src } },
                },
            },
        };

        var attempted = new GeneratedProfileCounts(
            new Dictionary<string, int>(StringComparer.Ordinal) { [src] = 3 });   // 2 complete + 1 truncated tail
        var skips = new Dictionary<string, int>(StringComparer.Ordinal) { [src] = 1 };

        Dictionary<string, int> expected = GenerativeTruth.BuildExpected(config, attempted, skips);

        Assert.Equal(2, expected[FolderPathKey.Join(new[] { "Inbox" })]);   // 3 - 1
    }

    // Aggregate per-folder counts from the INDEPENDENT reader across all output parts, then assert
    // every expected path matches exactly and no unexpected folder appears (except known zero-count
    // store folders). Extracted here so the corruption case (Task 2) reuses the exact comparison.
    private static void AssertIndependentCountsMatch(
        IReadOnlyList<string> outputs, Dictionary<string, int> expected)
    {
        var actual = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string part in outputs)
        {
            ValidatorResult r = PstValidatorRunner.Run(part, Timeout);
            Assert.True(r.Opened, $"validator could not open {Path.GetFileName(part)}: " +
                string.Join("; ", r.Errors.Select(e => $"{e.Stage}:{e.Message}")));
            Assert.Empty(r.Errors);
            foreach (ValidatedFolder vf in r.Folders)
            {
                string key = FolderPathKey.Join(vf.Path);
                actual[key] = actual.GetValueOrDefault(key) + vf.MessageCount;
            }
        }

        foreach ((string key, int count) in expected)
        {
            Assert.True(actual.TryGetValue(key, out long got), $"expected folder '{key}' missing");
            Assert.Equal(count, (int)got);
        }
        foreach ((string key, long got) in actual)
        {
            if (expected.ContainsKey(key)) continue;
            if (got == 0 && ZeroCountAllowlist.Contains(key)) continue;
            Assert.Fail($"unexpected folder '{key}' with {got} message(s)");
        }
    }
}

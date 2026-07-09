// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.TestSupport;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

[Trait("Category", "IndependentValidation")]
public class IndependentValidationTests
{
    // Zero-count folders the independent reader may surface that the converter did not create
    // (known template/system folders). Any zero-count surprise fails until deliberately
    // allowlisted here with a comment explaining why it exists.
    private static readonly HashSet<string> ZeroCountAllowlist = new(StringComparer.Ordinal)
    {
        // The from-scratch store (PSTFile.CreateEmptyStore) seeds a default "Deleted Items"
        // folder under the IPM subtree, which the independent reader surfaces with 0 messages.
        // (Replaces the retired Outlook template's Danish "Slettet post".)
        FolderPathKey.Join(new[] { "Deleted Items" }),
        // A mail conversion bakes the category master list (the CategoryList FAI — mail-tag colours
        // plus the yellow "Star" category) into a top-level "Calendar" folder, created for that
        // purpose when the store has none. It carries only the associated FAI message, so the
        // independent reader (which counts regular messages) surfaces it with 0 messages.
        FolderPathKey.Join(new[] { "Calendar" }),
    };

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [SkippableFact]
    public void ConverterOutput_ValidatesWithIndependentReader()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe to run the independent-reader gate.");

        // Build a config over the committed sample mbox (mirror mode → one folder per source).
        ConversionConfig config = SampleConfig(out string outDir);
        try { ConvertAndAssertIndependentCountsMatchTruth(config, outDir); }
        finally { Directory.Delete(outDir, true); }
    }

    // A from-scratch empty store (no messages written) must itself validate clean with the
    // INDEPENDENT MS-PST reader. This isolates CreateEmptyStore's scaffold/blueprint validity from
    // the message-write path that ConverterOutput_ValidatesWithIndependentReader exercises.
    [SkippableFact]
    public void CreateEmptyStore_BareStore_ValidatesWithIndependentReader()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe to run the independent-reader gate.");

        string path = Path.Combine(Path.GetTempPath(), $"m2p-empty-{Guid.NewGuid():N}.pst");
        try
        {
            PSTFileFormat.PSTFile.CreateEmptyStore(path);
            ValidatorResult r = PstValidatorRunner.Run(path, Timeout);
            Assert.True(r.Opened,
                "validator could not open bare empty store: " +
                string.Join("; ", r.Errors.Select(e => $"{e.Stage}:{e.Message}")));
            Assert.Empty(r.Errors);
        }
        finally { File.Delete(path); }
    }

    // [Tier 1] Structural canary: a tiny profile with shapes sample.mbox lacks — a multi-folder
    // tree, a non-ASCII (Danish) folder display name, and an empty folder with
    // IncludeEmptyFolders=true — converted through the real ConversionRunner and validated by the
    // INDEPENDENT MS-PST reader. Uniquely detects: multi-folder enumeration drift, PT_UNICODE
    // folder-name corruption, and whether the independent reader enumerates converter-created
    // empty folders.
    [SkippableFact]
    [Trait("Tier", "1")]
    public void GeneratedProfile_NastyShapes_ValidatesWithIndependentReader()
    {
        Skip.If(PstValidatorRunner.ValidatorPath is null,
            "Set MAIL2PST_PST_VALIDATOR to the built pst-validate exe to run the independent-reader gate.");

        using var profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("Inbox", messageCount: 3)                 // ASCII, multi-message
            .WithFolder("Færdige opgaver", messageCount: 2)       // Danish PT_UNICODE folder name
            .WithFolder("Kladder", messageCount: 0)               // empty folder (C5 output side)
            .Build();

        // Guard against generator API drift: prove the profile really has the three intended shapes.
        Assert.Equal(3, profile.Folders.Count);
        Assert.Contains(profile.Folders, f => Path.GetFileName(f.FilePath) == "Færdige opgaver");
        Assert.Contains(profile.Folders, f => Path.GetFileName(f.FilePath) == "Kladder");

        string outDir = Path.Combine(Path.GetTempPath(), "mail2pst-canary-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        var config = new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Archive",
                    MaxSizeMB = 50_000,
                    FolderMapping = FolderMappingMode.Mirror,
                    IncludeEmptyFolders = true, // exercises empty-folder enumeration through both truth and reader
                    Sources = profile.Folders
                        .Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath })
                        .ToList(),
                },
            },
        };

        // C5 GUARD: prove this canary is actually exercising the empty-folder branch. If BuildTruth
        // itself dropped the empty folder, the expected model would silently stop testing C5 while
        // the test still passed — assert the expected model includes it, count 0.
        Dictionary<string, int> expected = RoundTripHarness.BuildTruth(config)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);
        Assert.True(expected.ContainsKey("Kladder"),
            "Canary is intended to exercise IncludeEmptyFolders=true, but BuildTruth did not include the empty folder.");
        Assert.Equal(0, expected["Kladder"]);

        try { ConvertAndAssertIndependentCountsMatchTruth(config, outDir); }
        finally { Directory.Delete(outDir, true); }
    }

    // Converts `config` into `outDir`, validates every produced PST part with the INDEPENDENT
    // reader, and asserts the independent per-folder counts exactly match the converter's own
    // BuildTruth. Shared by the sample-mbox gate and the generated-profile canary.
    private static void ConvertAndAssertIndependentCountsMatchTruth(ConversionConfig config, string outDir)
    {
        var (outputs, _) = RoundTripHarness.Convert(config, outDir);
        Assert.NotEmpty(outputs); // a conversion that produced no PST parts would otherwise pass vacuously

        // Expected path-keyed counts from the converter's own truth model.
        Dictionary<string, int> expected = RoundTripHarness.BuildTruth(config)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Count, StringComparer.Ordinal);

        // Actual path-keyed counts, aggregated across all output parts via the INDEPENDENT reader.
        var actual = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string part in outputs)
        {
            ValidatorResult r = PstValidatorRunner.Run(part, Timeout);
            Assert.True(r.Opened, $"validator could not open {Path.GetFileName(part)}: " +
                string.Join("; ", r.Errors.Select(e => $"{e.Stage}:{e.Message}")));
            Assert.Empty(r.Errors);
            foreach (ValidatedFolder f in r.Folders)
            {
                string key = FolderPathKey.Join(f.Path);
                actual[key] = actual.GetValueOrDefault(key) + f.MessageCount;
            }
        }

        // Every expected path must match exactly (includes expected empty folders).
        foreach ((string key, int count) in expected)
        {
            Assert.True(actual.TryGetValue(key, out long got),
                $"expected folder '{key}' [{CodePoints(key)}] missing from independent reader output. " +
                $"actual keys: {string.Join(" | ", actual.Keys.Select(k => $"'{k}' [{CodePoints(k)}]"))}");
            Assert.Equal(count, got);
        }

        // Any unexpected folder is a failure unless it is a known zero-count implementation folder.
        foreach ((string key, long got) in actual)
        {
            if (expected.ContainsKey(key)) continue;
            if (got == 0 && ZeroCountAllowlist.Contains(key)) continue;
            Assert.Fail($"unexpected folder '{key}' with {got} message(s) in independent reader output");
        }
    }

    // Diagnostic: render a string's UTF-16 code units as hex so NFC/NFD or encoding drift is visible.
    private static string CodePoints(string s) => string.Join("+", s.Select(c => ((int)c).ToString("X4")));

    private static ConversionConfig SampleConfig(out string outDir)
    {
        outDir = Path.Combine(Path.GetTempPath(), "mail2pst-indep-" + Guid.NewGuid());
        Directory.CreateDirectory(outDir);
        string sample = RepoPaths.ResolveAgainstRepoRoot(Path.Combine("fixtures", "sample.mbox"));
        return new ConversionConfig
        {
            Outputs = new List<OutputGroupConfig>
            {
                new()
                {
                    Name = "Archive",
                    MaxSizeMB = 50_000,
                    FolderMapping = FolderMappingMode.Mirror,
                    // IncludeEmptyFolders = false for the first gate: BuildTruth is prefix-aware and
                    // would expect structural/empty folders, but it is not yet confirmed that the
                    // independent reader enumerates empty folders. sample.mbox is non-empty (mirror
                    // => one non-empty folder), so the comparison does not depend on empty folders.
                    IncludeEmptyFolders = false,
                    Sources = new List<SourceConfig> { new() { Type = "mbox", Path = sample } },
                },
            },
        };
    }
}

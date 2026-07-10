// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Xunit;

namespace Mail2Pst.Property.Tests;

public class RunnerInvariantTests
{
    private static string NewScratch() =>
        Path.Combine(Path.GetTempPath(), $"m2p-prop-{Guid.NewGuid():N}");

    [Fact]
    public void Factory_ValidRecipe_BuildsRunnableConfigWithMboxFileOnDisk()
    {
        var recipe = new ConfigRecipe(new[]
        {
            new OutputRecipe(NameKind: 0, MaxSizeMB: 20000, Mirror: true, IncludeEmpty: true,
                SourcesKind: 2, Sources: new[] { new SourceRecipe(TargetKind: 0) }),
        });
        string scratch = NewScratch();
        try
        {
            ConversionConfig cfg = ConfigFactory.Build(recipe, scratch);
            Assert.Single(cfg.Outputs);
            Assert.Equal("Output0", cfg.Outputs[0].Name);
            Assert.Single(cfg.Outputs[0].Sources);
            Assert.Equal("mbox", cfg.Outputs[0].Sources[0].Type);
            Assert.True(File.Exists(cfg.Outputs[0].Sources[0].Path));  // real mbox on disk
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    [Fact]
    public void Factory_ReservedNameRecipe_ProducesConfigConfigValidatorRejects()
    {
        var recipe = new ConfigRecipe(new[]
        {
            new OutputRecipe(NameKind: 1, MaxSizeMB: 20000, Mirror: false, IncludeEmpty: true,
                SourcesKind: 2, Sources: new[] { new SourceRecipe(TargetKind: 0) }),
        });
        string scratch = NewScratch();
        try
        {
            ConversionConfig cfg = ConfigFactory.Build(recipe, scratch);
            Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(cfg));
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    // Deterministic #8-adjacent lock (ConfigValidator side): a null Sources list must be handled
    // cleanly — ConfigValidator rejects it with a "no sources" ConfigValidationException, NEVER a
    // NullReferenceException. If ConfigValidator's `Sources ?? new List<>()` guard were removed, this
    // throws NRE instead and the test goes red. (The MappingEngine side is locked separately below.)
    [Fact]
    public void Factory_NullSourcesRecipe_ValidatorRejectsCleanlyWithoutNre()
    {
        var recipe = new ConfigRecipe(new[]
        {
            new OutputRecipe(NameKind: 0, MaxSizeMB: 20000, Mirror: true, IncludeEmpty: true,
                SourcesKind: 0, Sources: Array.Empty<SourceRecipe>()),
        });
        string scratch = NewScratch();
        try
        {
            ConversionConfig cfg = ConfigFactory.Build(recipe, scratch);
            Assert.Null(cfg.Outputs[0].Sources);                                   // knob produced null
            Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(cfg));
        }
        finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
    }

    // Deterministic #8 lock (MappingEngine side): a null Sources list must not NRE inside the planner.
    // The ConfigValidator test above never reaches MappingEngine (a source-less/contact-less config is
    // rejected first), so this drives BuildPlan directly with null Sources + a contact mapping (so the
    // output still contributes something). Locks MappingEngine's `output.Sources ?? new List<>()` guard.
    [Fact]
    public void MappingEngine_NullSources_WithContactMapping_DoesNotNre()
    {
        var cfg = new ConversionConfig
        {
            Outputs =
            {
                new OutputGroupConfig
                {
                    Name = "Output0",
                    MaxSizeMB = 20000,
                    Sources = null!,
                    Contacts =
                    {
                        new ContactSourceConfig { Path = "contacts.sqlite", Format = "thunderbird-sqlite" },
                    },
                },
            },
        };

        List<PstOutputPlan> plans = MappingEngine.BuildPlan(cfg);   // must not throw

        Assert.Single(plans);
        Assert.Empty(plans[0].SourceMappings);
        Assert.Single(plans[0].ContactMappings);
    }
}

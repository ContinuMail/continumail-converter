// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Text;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Writing;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class PstPartManagerOutputExistsTests
{
    private static readonly byte[] PriorBytes =
        Encoding.UTF8.GetBytes("PRIOR GOOD PST DATA — this run must not truncate or delete it");

    private static PstPartManager NewManager(string groupName, string dir) =>
        new PstPartManager(groupName, dir, long.MaxValue, 500,
            writeMessage: (f, fo, m) => { }, writeContact: (f, fo, c) => { });

    [Fact]
    public void Begin_OutputPstAlreadyExists_ThrowsAndLeavesItByteForByteIntact()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-exists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string existing = Path.Combine(dir, "Archive.pst");
        File.WriteAllBytes(existing, PriorBytes);
        try
        {
            var mgr = NewManager("Archive", dir);
            var ex = Assert.Throws<ConfigValidationException>(() => mgr.Begin(Array.Empty<FolderToPrecreate>()));
            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Archive.pst", ex.Message);
            // The pre-existing PST must be byte-for-byte intact — never truncated by CreateEmptyStore.
            Assert.Equal(PriorBytes, File.ReadAllBytes(existing));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Begin_ExistingSplitPartFromPriorRun_ThrowsAndLeavesItIntact()
    {
        // A prior run split into Archive-1.pst (no bare Archive.pst). A re-run must still fail fast.
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-exists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string part1 = Path.Combine(dir, "Archive-1.pst");
        File.WriteAllBytes(part1, PriorBytes);
        try
        {
            var mgr = NewManager("Archive", dir);
            var ex = Assert.Throws<ConfigValidationException>(() => mgr.Begin(Array.Empty<FolderToPrecreate>()));
            Assert.Contains("Archive-1.pst", ex.Message);
            Assert.Equal(PriorBytes, File.ReadAllBytes(part1));
            Assert.False(File.Exists(Path.Combine(dir, "Archive.pst")));   // nothing created
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Begin_UnrelatedDashPstName_DoesNotBlock_AndCreatesOutput()
    {
        // "Archive-old.pst" is NOT a converter-owned split part (non-numeric suffix) — must not block.
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-exists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "Archive-old.pst"), PriorBytes);
        try
        {
            var mgr = NewManager("Archive", dir);
            mgr.Begin(Array.Empty<FolderToPrecreate>());   // must NOT throw
            mgr.Finish();
            mgr.Close();
            Assert.True(File.Exists(Path.Combine(dir, "Archive.pst")));           // created cleanly
            Assert.Equal(PriorBytes, File.ReadAllBytes(Path.Combine(dir, "Archive-old.pst")));  // untouched
        }
        finally { Directory.Delete(dir, true); }
    }

    // Locks the numeric edge of the split pattern: parts are Name-[1-9][0-9]*.pst (numbered from 1,
    // no leading zero). A leading-zero name is NOT a converter-owned part and must not block. Guards
    // against a future loosening of the regex to \d+ (which would be a user-visible behaviour change).
    [Theory]
    [InlineData("Archive-0.pst")]
    [InlineData("Archive-01.pst")]
    public void Begin_LeadingZeroNumericLikePstName_DoesNotBlock(string fileName)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-exists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, fileName), PriorBytes);
        try
        {
            var mgr = NewManager("Archive", dir);
            mgr.Begin(Array.Empty<FolderToPrecreate>());   // must NOT throw
            mgr.Finish();
            mgr.Close();
            Assert.True(File.Exists(Path.Combine(dir, "Archive.pst")));           // created cleanly
            Assert.Equal(PriorBytes, File.ReadAllBytes(Path.Combine(dir, fileName)));  // untouched
        }
        finally { Directory.Delete(dir, true); }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Integration.Tests;

/// <summary>
/// Regression: a source mail folder that maps to the target name "Deleted Items" must NOT fail
/// the conversion. The blank from-scratch store (DefaultStoreTemplates) seeds "Deleted Items" as
/// a child of Top of Information Store with an EMPTY PidTagContainerClass; every writer-created
/// folder gets a real IPF class, so an empty class uniquely marks an adoptable seeded special.
/// PstPartManager.GuardLeafClass used to throw "already exists as ''; cannot reuse it as
/// 'IPF.Note'" — a hard conversion failure hit by any Thunderbird/IMAP account whose trash folder
/// is literally "Deleted Items" (2026-07-08). (The store's other seeded special, "Search Root",
/// hangs off the Root Folder rather than Top of Information Store, so it never collides here.)
/// </summary>
public class DeletedItemsFolderReuseTests
{
    [Fact]
    public void SourceFolderNamedDeletedItems_IsAdopted_NotRejected()
    {
        const string folderName = "Deleted Items";
        string outDir = Path.Combine(Path.GetTempPath(), "m2p-special-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var plan = new PstOutputPlan { Name = "P", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = true };
            var planned = new[]
            {
                new PlannedMessage { Message = Msg("Trashed mail"), TargetFolderPath = new[] { folderName } },
            };
            var report = new ConversionReport();

            IReadOnlyList<string> psts = new PstWriter().WritePlan(plan, planned, outDir, report);

            Assert.Single(psts);
            var pst = new PSTFile(psts[0], FileAccess.Read);
            try
            {
                PSTFolder? adopted = pst.TopOfPersonalFolders.FindChildFolder(folderName);
                Assert.NotNull(adopted);
                // The message landed in the adopted special folder (fidelity: deleted mail -> Deleted Items).
                Assert.Equal(1, adopted!.MessageCount);
            }
            finally { pst.CloseFile(); }
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static MailMessage Msg(string subject) => new()
    {
        Subject = subject,
        From = new MailAddress { Name = "Sender", Email = "sender@example.com" },
        To = { new MailAddress { Name = "Rcpt", Email = "rcpt@example.com" } },
        Date = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        TextBody = "Body.",
    };
}

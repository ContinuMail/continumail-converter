// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Reporting;
using Mail2Pst.Core.Writing;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.Writing;

public class WritePlanContactPhaseTests
{
    [Fact]
    public void WritePlan_WritesContactsAfterMail_IntoSameStore()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-wp-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var contactFolders = new List<IReadOnlyList<string>> { new[] { "Contacts", "Personal Address Book" } };
            var contacts = new List<PlannedContact>
            {
                new() { Contact = new ContactRecord { DisplayName = "Frank" },
                        TargetFolderPath = new[] { "Contacts", "Personal Address Book" } },
            };
            var report = new ConversionReport();

            new PstWriter().WritePlan(plan, new List<PlannedMessage>(), contacts, contactFolders, dir, report);

            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                PSTFolder cf = f.TopOfPersonalFolders.FindChildFolder("Contacts").FindChildFolder("Personal Address Book");
                Assert.Equal("IPF.Contact", cf.ContainerClass);
                Assert.Equal(1, report.ContactsConverted);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WritePlan_EmptyAddressBook_StillCreatesContactFolder()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"m2p-wpe-{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var plan = new PstOutputPlan { Name = "Out", MaxSizeBytes = long.MaxValue };
            var contactFolders = new List<IReadOnlyList<string>> { new[] { "Contacts", "Empty Book" } };
            var report = new ConversionReport();

            new PstWriter().WritePlan(plan, new List<PlannedMessage>(), new List<PlannedContact>(), contactFolders, dir, report);

            PSTFile? f = null;
            try
            {
                f = new PSTFile(Path.Combine(dir, "Out.pst"), FileAccess.Read);
                PSTFolder cf = f.TopOfPersonalFolders.FindChildFolder("Contacts").FindChildFolder("Empty Book");
                Assert.Equal("IPF.Contact", cf.ContainerClass);
            }
            finally { f?.CloseFile(); }
        }
        finally { Directory.Delete(dir, true); }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Xunit;

namespace Mail2Pst.Core.Tests.Config;

// Regression guard: an output group made entirely of PIM data (contacts/calendars, no mail
// Sources) must be accepted by ConfigValidator — and a group with none of sources/contacts/
// calendars must still be rejected. Pins the "!hasMail && !hasContacts && !hasTasks &&
// !hasAppointments" guard in ConfigValidator.Validate.
public class ConfigValidatorPimOnlyTests
{
    private static ConversionConfig With(OutputGroupConfig g) =>
        new() { Outputs = new List<OutputGroupConfig> { g } };

    [Fact]
    public void Accepts_group_with_contacts_and_no_mail()
    {
        var g = new OutputGroupConfig
        {
            Name = "Local Folders", MaxSizeMB = 5120, FolderMapping = FolderMappingMode.Mirror,
            IncludeEmptyFolders = true, Sources = new List<SourceConfig>(),
            Contacts = new List<ContactSourceConfig>
            {
                new() { Path = "/p/abook.sqlite", Format = "thunderbird-sqlite" },
            },
        };

        ConfigValidator.Validate(With(g)); // must not throw
    }

    [Fact]
    public void Rejects_group_with_nothing()
    {
        var g = new OutputGroupConfig
        {
            Name = "Empty", MaxSizeMB = 5120, FolderMapping = FolderMappingMode.Mirror,
            IncludeEmptyFolders = true, Sources = new List<SourceConfig>(),
        };

        Assert.Throws<ConfigValidationException>(() => ConfigValidator.Validate(With(g)));
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class ConfigFromDiscoveryContactTests
{
    private static DiscoveryResult Sample() => new(
        Root: "/data/tb/profile",
        Layout: "thunderbird-profile",
        Sources: new[]
        {
            new DiscoveredSource("/p/Inbox", "mbox", new[] { "Local Folders", "Inbox" }, "Inbox", 10, "/p/Inbox.msf"),
        },
        Warnings: Array.Empty<DiscoveryWarning>(),
        Skipped: Array.Empty<DiscoverySkipped>(),
        Pairing: new DiscoveryPairingSummary(1, 0, 0))
    {
        AddressBooks = new List<DiscoveredAddressBook>
        {
            new() { DisplayName = "Personal Address Book", Path = "/p/abook.sqlite", Format = "thunderbird-sqlite" },
        },
    };

    [Fact]
    public void Build_IncludeContacts_SynthesizesContactSources()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), includeContacts: true);
        Assert.Contains(config.Outputs.SelectMany(o => o.Contacts), c => c.Path == "/p/abook.sqlite");
    }

    [Fact]
    public void Build_NoContacts_OmitsContactSources()
    {
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), includeContacts: false);
        Assert.Empty(config.Outputs.SelectMany(o => o.Contacts));
    }

    [Fact]
    public void Build_TemplateContactsWin_SynthesisSkippedForThatGroup()
    {
        var template = new ConversionConfig
        {
            Outputs =
            {
                new OutputGroupConfig
                {
                    Contacts =
                    {
                        new ContactSourceConfig { Path = "/explicit/custom.sqlite", Format = "thunderbird-sqlite" },
                    },
                },
            },
        };
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), template, includeContacts: true);
        var contacts = config.Outputs.SelectMany(o => o.Contacts).ToList();
        // The explicit template contact is preserved; the discovered one is NOT added (template wins).
        Assert.Single(contacts);
        Assert.Equal("/explicit/custom.sqlite", contacts[0].Path);
    }

    [Fact]
    public void Build_TemplateContacts_CopiedEvenWhenIncludeContactsFalse()
    {
        var template = new ConversionConfig
        {
            Outputs =
            {
                new OutputGroupConfig
                {
                    Contacts =
                    {
                        new ContactSourceConfig { Path = "/explicit/custom.sqlite", Format = "thunderbird-sqlite" },
                    },
                },
            },
        };
        // With includeContacts:false, synthesis is skipped — but explicit template contacts must still be copied.
        ConversionConfig config = ConfigFromDiscovery.Build(Sample(), template, includeContacts: false);
        var contacts = config.Outputs.SelectMany(o => o.Contacts).ToList();
        Assert.Single(contacts);
        Assert.Equal("/explicit/custom.sqlite", contacts[0].Path);
    }
}

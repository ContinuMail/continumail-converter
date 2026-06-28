// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Contacts;

namespace Mail2Pst.Core.Mapping;

public class ContactMapping
{
    public ContactSourceConfig Source { get; set; } = new();
    public IReadOnlyList<string> TargetFolderPath { get; set; } = Array.Empty<string>();
    public AddressBookFormat Format { get; set; }
}

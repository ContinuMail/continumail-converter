// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Contacts;

public interface IAddressBookReader
{
    IEnumerable<ContactReadResult> Read(AddressBook book);
}

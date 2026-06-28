// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
namespace Mail2Pst.Core.Contacts;

public class AddressBook
{
    public string DisplayName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public AddressBookFormat Format { get; set; }
}

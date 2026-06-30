/* Copyright (C) 2012-2016 ROM Knowledgeware. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 * 
 * Maintainer: Tal Aloni <tal@kmrom.com>
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace PSTFileFormat
{
    public class MessageRecipient
    {
        public string DisplayName;
        public string EmailAddress;
        public bool IsOrganizer;
        public RecipientType RecipientType = RecipientType.To; // default preserves prior behavior
        public int ResponseStatus = 0; // PidTagRecipientTrackStatus; 0 = none/unknown (default, backward-compat)

        public MessageRecipient()
        {
        }

        public MessageRecipient(string displayName, string emailAddress, bool isOrganizer)
        {
            DisplayName = displayName;
            EmailAddress = emailAddress;
            IsOrganizer = isOrganizer;
        }

        public MessageRecipient(string displayName, string emailAddress, bool isOrganizer, RecipientType recipientType)
        {
            DisplayName = displayName;
            EmailAddress = emailAddress;
            IsOrganizer = isOrganizer;
            RecipientType = recipientType;
        }
    }
}

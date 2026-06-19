/* Copyright (C) 2012-2016 ROM Knowledgeware. All rights reserved.
 *
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 *
 * Maintainer: Tal Aloni <tal@kmrom.com>
 */
// ContinuMail modification 2026: added string-named (MNID_STRING) property support.
// See vendor/PSTFileFormat-MODIFICATIONS.md for details.
using System;
using System.Collections.Generic;
using System.Text;

namespace PSTFileFormat
{
    public class PropertyName
    {
        public PropertyLongID PropertyLongID;
        public Guid PropertySetGuid;

        // String-name (MNID_STRING) fields — null/false for numeric (MNID_ID) names.
        public string? PropertyStringName;
        public bool IsStringIdentifier;

        /// <summary>Numeric (MNID_ID) named property.</summary>
        public PropertyName(PropertyLongID propertyLongID, Guid propertySetGuid)
        {
            PropertyLongID = propertyLongID;
            PropertySetGuid = propertySetGuid;
            IsStringIdentifier = false;
        }

        /// <summary>String-named (MNID_STRING) named property.</summary>
        public PropertyName(string propertyStringName, Guid propertySetGuid)
        {
            PropertyStringName = propertyStringName;
            PropertySetGuid = propertySetGuid;
            IsStringIdentifier = true;
        }

        public override bool Equals(object obj)
        {
            if (obj is PropertyName other)
            {
                if (IsStringIdentifier != other.IsStringIdentifier) return false;
                if (PropertySetGuid != other.PropertySetGuid) return false;
                if (IsStringIdentifier)
                    return string.Equals(PropertyStringName, other.PropertyStringName, StringComparison.Ordinal);
                return PropertyLongID == other.PropertyLongID;
            }
            return false;
        }

        public override int GetHashCode()
        {
            if (IsStringIdentifier)
                return HashCode.Combine(IsStringIdentifier, PropertySetGuid, PropertyStringName ?? string.Empty);
            return HashCode.Combine(IsStringIdentifier, PropertySetGuid, PropertyLongID);
        }
    }
}

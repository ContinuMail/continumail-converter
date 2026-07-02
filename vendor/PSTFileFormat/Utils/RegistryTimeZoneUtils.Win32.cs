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
using Microsoft.Win32;

namespace Utilities
{
    public partial class RegistryTimeZoneUtils
    {
        /// <summary>
        /// For dynamic DST use TimeZoneInfo
        /// </summary>
        /// /// <returns>null if the key does not contain timezone information</returns>
        public static RegistryTimeZoneInformation GetStaticTimeZoneInformation(string keyName)
        {
            // [ContinuMail 2026] Registry.LocalMachine is null off-Windows; short-circuit BEFORE touching it.
            if (!OperatingSystem.IsWindows()) return null;
            RegistryKey timeZonesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones");
            RegistryKey timeZoneKey = timeZonesKey?.OpenSubKey(keyName);
            if (timeZoneKey == null)
            {
                return null;
            }

            byte[] tzi = (byte[])timeZoneKey.GetValue("TZI");
            if (tzi == null)
            {
                return null;
            }

            return new RegistryTimeZoneInformation(tzi);
        }

        public static string GetDisplayName(string keyName, out string standardDisplayName, out string daylightDisplayName)
        {
            // [ContinuMail 2026] Registry.LocalMachine is null off-Windows; short-circuit BEFORE touching it.
            if (!OperatingSystem.IsWindows())
            {
                standardDisplayName = null;
                daylightDisplayName = null;
                return null;
            }
            RegistryKey timeZonesKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Time Zones");
            RegistryKey timeZoneKey = timeZonesKey?.OpenSubKey(keyName); // [ContinuMail 2026] null-safe: no registry on non-Windows.
            if (timeZoneKey == null)
            {
                standardDisplayName = null;
                daylightDisplayName = null;
                return null;
            }

            string displayName = (string)timeZoneKey.GetValue("Display");
            standardDisplayName = (string)timeZoneKey.GetValue("Std");
            daylightDisplayName = (string)timeZoneKey.GetValue("Dlt");

            return displayName;
        }

        public static bool IsDaylightSavingsEnabled()
        {
            // [ContinuMail 2026] Registry.LocalMachine is null off-Windows; short-circuit BEFORE touching it.
            if (!OperatingSystem.IsWindows()) return true;
            RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\TimeZoneInformation");
            if (key == null)
            {
                return true;
            }
            int value = (int)key.GetValue("DisableAutoDaylightTimeSet", 0);
            return (value == 0);
        }
    }
}

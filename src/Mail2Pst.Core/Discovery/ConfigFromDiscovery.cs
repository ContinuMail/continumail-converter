// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;

namespace Mail2Pst.Core.Discovery;

/// <summary>
/// Builds a ConversionConfig from discovery for profile-mode conversion. With no template, uses
/// defaults. With a template, copies OPTIONS only (top-level + at most one output group's settings);
/// the template's sources are discarded — discovery supplies the sources. Reused by the SP4c GUI.
/// <para>When <paramref name="includeContacts"/> is true (default), discovered address books are
/// synthesized as <see cref="ContactSourceConfig"/> entries on each output group that has no
/// explicit template contacts. Explicit template contacts always win per group.</para>
/// <para>When <paramref name="includeTasks"/> is true (default), discovered calendars with
/// <see cref="DiscoveredCalendarSource.TaskCount"/> &gt; 0 are synthesized as
/// <see cref="CalendarSourceConfig"/> entries on each output group that has no explicit
/// template calendars. Explicit template calendars always win per group.</para>
/// <para>When <paramref name="includeAppointments"/> is true (default), discovered calendars with
/// <see cref="DiscoveredCalendarSource.EventCount"/> &gt; 0 are synthesized as
/// <see cref="CalendarSourceConfig"/> entries carrying appointment fields. One config per calendar
/// carries both appointments and tasks when both conditions are met. Explicit template calendars
/// always win per group.</para>
/// </summary>
public static class ConfigFromDiscovery
{
    public static ConversionConfig Build(DiscoveryResult discovery, ConversionConfig? template = null,
        bool includeContacts = true, bool includeTasks = true, bool includeAppointments = true)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        var config = new ConversionConfig();
        var group = new OutputGroupConfig();

        if (template is not null)
        {
            if (template.Outputs.Count > 1)
                throw new ConfigValidationException(
                    "Profile mode accepts a config template with at most one output group; " +
                    $"found {template.Outputs.Count}.");

            config.JunkHandling = template.JunkHandling;
            config.DropExpunged = template.DropExpunged;

            if (template.Outputs.Count == 1)
            {
                OutputGroupConfig t = template.Outputs[0];
                group.Name = t.Name;
                group.MaxSizeMB = t.MaxSizeMB;
                group.IncludeEmptyFolders = t.IncludeEmptyFolders;
                group.FolderMapping = t.FolderMapping;

                // Copy template contacts onto the group (template sources are discarded;
                // template contacts are preserved so "template wins" check below works).
                group.Contacts = (t.Contacts ?? new List<ContactSourceConfig>())
                    .Select(c => new ContactSourceConfig
                    {
                        Path = c.Path,
                        Format = c.Format,
                        TargetFolderPath = c.TargetFolderPath,
                    }).ToList();
            }
        }

        if (string.IsNullOrWhiteSpace(group.Name))
            group.Name = DeriveName(discovery.Root);

        group.Sources = discovery.Sources.Select(s => new SourceConfig
        {
            Path = s.Path,
            Type = s.Type,
            TargetFolderPath = s.TargetFolderPath.ToList(),
            MsfPath = s.MsfPath,
        }).ToList();

        config.ProfilePath = discovery.Root;   // --profile root is authoritative (ignores any template ProfilePath)

        config.Outputs.Add(group);

        // Synthesize contact sources from discovered address books.
        // Explicit template contacts win per group — skip synthesis if the group already has contacts.
        if (includeContacts)
        {
            foreach (OutputGroupConfig output in config.Outputs)
            {
                if (output.Contacts.Count > 0) continue; // explicit template contacts win for this group
                foreach (DiscoveredAddressBook book in discovery.AddressBooks)
                    output.Contacts.Add(new ContactSourceConfig
                    {
                        Path = book.Path,
                        Format = book.Format,
                        TargetFolderPath = new[] { "Contacts", book.DisplayName },
                    });
            }
        }

        // Synthesize calendar sources from discovered calendars.
        // One CalendarSourceConfig per source carries whichever of appointments/tasks it has.
        // Explicit template calendars win per group — skip synthesis if the group already has calendars.
        if (includeTasks || includeAppointments)
        {
            foreach (OutputGroupConfig output in config.Outputs)
            {
                if (output.Calendars.Count > 0) continue; // explicit template calendars win for this group
                foreach (DiscoveredCalendarSource src in discovery.Calendars)
                {
                    bool wantAppts = includeAppointments && src.EventCount > 0;
                    bool wantTasks = includeTasks && src.TaskCount > 0;
                    if (!wantAppts && !wantTasks) continue;

                    output.Calendars.Add(new CalendarSourceConfig
                    {
                        StorePath = src.StorePath,
                        CalId = src.CalId,
                        IncludeAppointments = wantAppts,
                        AppointmentFolderPath = wantAppts ? src.DefaultCalendarFolderPath.ToArray() : null,
                        IncludeTasks = wantTasks,
                        TaskFolderPath = wantTasks ? src.DefaultTaskFolderPath.ToArray() : null,
                    });
                }
            }
        }

        return config;
    }

    private static string DeriveName(string root)
    {
        string name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? "Thunderbird" : name;
    }
}

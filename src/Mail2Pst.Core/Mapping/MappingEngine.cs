// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Contacts;

namespace Mail2Pst.Core.Mapping;

public static class MappingEngine
{
    /// <summary>
    /// Folder name used for sources in a "flatten" group that don't have a
    /// per-source <see cref="SourceConfig.TargetFolder"/> override.
    /// </summary>
    public const string DefaultFlattenFolderName = "Imported Mail";

    public static List<PstOutputPlan> BuildPlan(ConversionConfig config)
    {
        var plans = new List<PstOutputPlan>();

        foreach (OutputGroupConfig output in config.Outputs)
        {
            var plan = new PstOutputPlan
            {
                Name = output.Name,
                MaxSizeBytes = output.MaxSizeMB * 1024L * 1024L,
                IncludeEmptyFolders = output.IncludeEmptyFolders,
            };

            foreach (SourceConfig source in output.Sources)
            {
                IReadOnlyList<string> targetPath;
                if (source.TargetFolderPath is not null)
                    // An explicit path (even if empty/invalid) is treated as explicit — never
                    // silently falls back to a mode default. Invalid explicit targets are rejected
                    // up front by ConfigValidator in the real pipeline, and by GetOrCreateFolder's
                    // guard at write time; we do NOT mask them here by reverting to mode behaviour.
                    targetPath = source.TargetFolderPath.ToArray();        // copy: immutable snapshot
                else if (source.TargetFolder is not null)
                    // Same rule for the flat shorthand: an explicit (even empty) TargetFolder stays
                    // explicit. Using `is not null` (not IsNullOrEmpty) so an invalid "" is not
                    // silently converted into a mirror/flatten default.
                    targetPath = new[] { source.TargetFolder };
                else if (output.FolderMapping == FolderMappingMode.Mirror)
                    targetPath = new[] { Path.GetFileNameWithoutExtension(source.Path) };
                else
                    targetPath = new[] { DefaultFlattenFolderName };

                plan.SourceMappings.Add(new SourceMapping { Source = source, TargetFolderPath = targetPath });
            }

            // Build contact mappings.
            foreach (ContactSourceConfig contact in output.Contacts ?? new List<ContactSourceConfig>())
            {
                IReadOnlyList<string> target = contact.TargetFolderPath
                    ?? new[] { "Contacts", Path.GetFileNameWithoutExtension(contact.Path) };
                plan.ContactMappings.Add(new ContactMapping
                {
                    Source = contact,
                    TargetFolderPath = target,
                    Format = contact.Format == "thunderbird-mab"
                        ? AddressBookFormat.ThunderbirdMab : AddressBookFormat.ThunderbirdSqlite,
                });
            }

            // Plan-stage item-type collision check: a contact folder path must not equal a mail folder path.
            var mailPathKeys = new HashSet<string>(
                plan.SourceMappings.Select(m => FolderPathKey.Join(m.TargetFolderPath)),
                StringComparer.OrdinalIgnoreCase);
            foreach (ContactMapping cm in plan.ContactMappings)
            {
                string key = FolderPathKey.Join(cm.TargetFolderPath);
                if (mailPathKeys.Contains(key))
                    throw new ConfigValidationException(
                        $"Contact folder '{key}' collides with a mail folder of a different item type in output '{output.Name}'.");
            }

            // Build task mappings.
            foreach (CalendarSourceConfig cal in output.Calendars ?? new List<CalendarSourceConfig>())
            {
                if (!cal.IncludeTasks)
                    continue;
                IReadOnlyList<string> target = cal.TaskFolderPath
                    ?? new[] { "Tasks", cal.CalId };
                plan.TaskMappings.Add(new TaskMapping
                {
                    Source = cal,
                    TargetFolderPath = target,
                });
            }

            // Build appointment mappings.
            foreach (CalendarSourceConfig cal in output.Calendars ?? new List<CalendarSourceConfig>())
            {
                if (!cal.IncludeAppointments)
                    continue;
                IReadOnlyList<string> target = cal.AppointmentFolderPath
                    ?? new[] { "Calendars", cal.CalId };
                plan.AppointmentMappings.Add(new AppointmentMapping
                {
                    Source = cal,
                    TargetFolderPath = target,
                });
            }

            // Plan-stage item-type collision check: task and appointment folder paths must not
            // equal a mail, contact, task, or appointment folder path of a different item type.
            // Build one unified key set covering mail + contacts, then check tasks and appointments
            // against each other and against that set.
            var mailAndContactKeys = new HashSet<string>(
                plan.SourceMappings.Select(m => FolderPathKey.Join(m.TargetFolderPath))
                    .Concat(plan.ContactMappings.Select(m => FolderPathKey.Join(m.TargetFolderPath))),
                StringComparer.OrdinalIgnoreCase);

            var taskKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TaskMapping tm in plan.TaskMappings)
            {
                string key = FolderPathKey.Join(tm.TargetFolderPath);
                if (mailAndContactKeys.Contains(key))
                    throw new ConfigValidationException(
                        $"Task folder '{key}' collides with a mail or contact folder of a different item type in output '{output.Name}'.");
                taskKeys.Add(key);
            }

            foreach (AppointmentMapping am in plan.AppointmentMappings)
            {
                string key = FolderPathKey.Join(am.TargetFolderPath);
                if (mailAndContactKeys.Contains(key) || taskKeys.Contains(key))
                    throw new ConfigValidationException(
                        $"Appointment folder '{key}' collides with a mail, contact, or task folder of a different item type in output '{output.Name}'.");
            }

            plans.Add(plan);
        }

        return plans;
    }
}

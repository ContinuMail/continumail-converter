// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Config;

/// <summary>
/// Validates a deserialized <see cref="ConversionConfig"/> before a run, so
/// problems surface as clear <see cref="ConfigValidationException"/> messages
/// rather than obscure failures mid-conversion. Checks config shape and source
/// readability; parser-type support is validated separately by the runner.
/// </summary>
public static class ConfigValidator
{
    public static void Validate(ConversionConfig config)
    {
        if (config.Outputs is null || config.Outputs.Count == 0)
            throw new ConfigValidationException("Config has no outputs; at least one output group is required.");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OutputGroupConfig output in config.Outputs)
        {
            // Name must be a safe file stem (separators, '..', invalid chars, reserved names).
            OutputNameValidator.Validate(output.Name);

            // Duplicate names map to the same output file(s) — reject (case-insensitive,
            // matching Windows file-name semantics).
            if (!seenNames.Add(output.Name))
                throw new ConfigValidationException($"Duplicate output name '{output.Name}'. Output names must be unique.");

            if (output.MaxSizeMB <= 0)
                throw new ConfigValidationException(
                    $"Output '{output.Name}' has maxSizeMB={output.MaxSizeMB}; it must be greater than 0.");

            bool hasMail = output.Sources is { Count: > 0 };
            bool hasContacts = output.Contacts is { Count: > 0 };
            if (!hasMail && !hasContacts)
                throw new ConfigValidationException(
                    $"Output '{output.Name}' has no sources and no contacts.");

            foreach (ContactSourceConfig contact in output.Contacts ?? new List<ContactSourceConfig>())
            {
                if (string.IsNullOrWhiteSpace(contact.Path))
                    throw new ConfigValidationException($"Output '{output.Name}' has a contact source with an empty path.");
                if (contact.Format is not ("thunderbird-sqlite" or "thunderbird-mab"))
                    throw new ConfigValidationException(
                        $"Output '{output.Name}' has a contact source with unknown format '{contact.Format}'.");
                if (contact.TargetFolderPath is not null)
                    FolderNameValidator.ValidatePath(contact.TargetFolderPath);
            }

            foreach (SourceConfig source in output.Sources ?? new List<SourceConfig>())
            {
                // Empty path is a config mistake that would otherwise crash with an
                // uncaught ArgumentException. A *missing* file is intentionally left to
                // the runner, which records it as a per-source skip (see
                // ConversionRunnerTests.Run_MissingSourceFile_RecordsSkipInsteadOfThrowing).
                if (string.IsNullOrWhiteSpace(source.Path))
                    throw new ConfigValidationException($"Output '{output.Name}' has a source with an empty path.");

                // Exactly one of targetFolder / targetFolderPath may be set. The engine —
                // not only the GUI — enforces folder-name safety; hand-written/CLI configs
                // bypass the GUI. Neither set is legal (name derived: mirror=filename stem;
                // flatten="Imported Mail"). NOTE: "set" means non-null even if also invalid,
                // so a source with BOTH fields present reports the ambiguity ("both set")
                // FIRST — intentional and stable, even if one field is itself invalid.
                bool hasPath = source.TargetFolderPath is not null;
                bool hasName = source.TargetFolder is not null;
                if (hasPath && hasName)
                    throw new ConfigValidationException(
                        $"Output '{output.Name}' has a source that sets both targetFolder and " +
                        "targetFolderPath; set only one.");
                if (hasPath)
                    FolderNameValidator.ValidatePath(source.TargetFolderPath);
                else if (hasName)
                    FolderNameValidator.Validate(source.TargetFolder);
            }
        }
    }
}

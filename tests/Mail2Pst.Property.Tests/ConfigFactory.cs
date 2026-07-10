// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mail2Pst.Core.Config;

namespace Mail2Pst.Property.Tests;

/// <summary>
/// Translates a primitive <see cref="ConfigRecipe"/> into a concrete <see cref="ConversionConfig"/>,
/// writing a tiny valid mbox file for each source under <paramref name="scratchRoot"/>. Pure and
/// deterministic given (recipe, scratchRoot): no CsCheck, no randomness of its own.
/// </summary>
public static class ConfigFactory
{
    // One well-formed mbox message (From-line + headers + blank + body), MimeKit-parseable.
    private const string OneMessage =
        "From sender@example.com Mon Jan  1 00:00:00 2020\r\nSubject: Hi\r\n\r\nbody\r\n\r\n";

    public static ConversionConfig Build(ConfigRecipe recipe, string scratchRoot)
    {
        Directory.CreateDirectory(scratchRoot);
        var cfg = new ConversionConfig();
        for (int oi = 0; oi < recipe.Outputs.Count; oi++)
        {
            OutputRecipe o = recipe.Outputs[oi];
            var group = new OutputGroupConfig
            {
                Name = NameFor(o.NameKind, oi),
                MaxSizeMB = o.MaxSizeMB,
                FolderMapping = o.Mirror ? FolderMappingMode.Mirror : FolderMappingMode.Flatten,
                IncludeEmptyFolders = o.IncludeEmpty,
            };
            // SourcesKind: 0 = null (must not NRE — the #8 guard), 1 = empty, 2 = generated.
            if (o.SourcesKind == 0)
            {
                group.Sources = null!;
            }
            else if (o.SourcesKind == 2)
            {
                for (int si = 0; si < o.Sources.Count; si++)
                {
                    string path = Path.Combine(scratchRoot, $"o{oi}s{si}.mbox");
                    File.WriteAllText(path, OneMessage + OneMessage, new UTF8Encoding(false));
                    group.Sources.Add(SourceFor(o.Sources[si].TargetKind, path));
                }
            }
            // SourcesKind == 1 leaves the default empty list.
            cfg.Outputs.Add(group);
        }
        return cfg;
    }

    private static string NameFor(int kind, int index) => kind switch
    {
        1 => "CON",           // reserved -> ConfigValidationException
        2 => "",              // empty -> ConfigValidationException
        3 => "Output0",       // duplicate of the index-0 valid name -> ConfigValidationException (if >1 output)
        _ => $"Output{index}",// valid unique
    };

    private static SourceConfig SourceFor(int targetKind, string path)
    {
        var s = new SourceConfig { Path = path, Type = "mbox" };
        switch (targetKind)
        {
            case 1: s.TargetFolder = "Kept"; break;                              // valid name
            case 2: s.TargetFolderPath = new List<string> { "A", "B" }; break;   // valid path
            case 3: s.TargetFolder = "Kept"; s.TargetFolderPath = new List<string> { "A" }; break; // both -> invalid
            case 4: s.TargetFolder = "CON"; break;                               // invalid reserved name
            case 5: s.TargetFolderPath = new List<string> { "A", "CON" }; break; // invalid path (reserved segment)
            default: break;                                                      // none
        }
        return s;
    }
}

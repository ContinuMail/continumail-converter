// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Property.Tests;

// NameKind:    0 = valid unique, 1 = reserved ("CON"), 2 = empty, 3 = duplicate-of-first-output
// SourcesKind: 0 = null list (exercises the #8 null-Sources guard), 1 = empty list, 2 = generated list
// TargetKind:  0 = none, 1 = valid TargetFolder, 2 = valid TargetFolderPath, 3 = BOTH set (invalid),
//              4 = invalid TargetFolder (reserved name), 5 = invalid TargetFolderPath (reserved segment)
public sealed record SourceRecipe(int TargetKind);

public sealed record OutputRecipe(
    int NameKind, long MaxSizeMB, bool Mirror, bool IncludeEmpty, int SourcesKind,
    IReadOnlyList<SourceRecipe> Sources);

public sealed record ConfigRecipe(IReadOnlyList<OutputRecipe> Outputs);

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core.Reverse;
using Xunit;

namespace Mail2Pst.Core.Tests.Reverse;

public class MboxTreePlannerTests
{
    private static IReadOnlyList<string> P(params string[] segments) => segments;

    [Fact]
    public void Plan_NestedTree_BuildsSbdLayout()
    {
        string root = Path.Combine("out", "root");
        var plan = MboxTreePlanner.Plan(new[] { P("A"), P("A", "B"), P("A", "B", "C") }, root);

        Assert.True(plan.TryGet(P("A"), out var a));
        Assert.Equal(Path.Combine(root, "A"), a!.MboxFilePath);
        Assert.Equal(Path.Combine(root, "A.sbd"), a.SbdDirPath);
        Assert.True(a.IsStructuralParent);

        Assert.True(plan.TryGet(P("A", "B"), out var b));
        Assert.Equal(Path.Combine(root, "A.sbd", "B"), b!.MboxFilePath);
        Assert.Equal(Path.Combine(root, "A.sbd", "B.sbd"), b.SbdDirPath);
        Assert.True(b.IsStructuralParent);

        Assert.True(plan.TryGet(P("A", "B", "C"), out var c));
        Assert.Equal(Path.Combine(root, "A.sbd", "B.sbd", "C"), c!.MboxFilePath);
        Assert.Null(c.SbdDirPath);                 // leaf: no .sbd
        Assert.False(c.IsStructuralParent);
    }

    [Fact]
    public void Plan_SynthesizesMissingAncestor()
    {
        // Only the child is listed; the planner must still materialize "A" as a structural parent.
        string root = "r";
        var plan = MboxTreePlanner.Plan(new[] { P("A", "B") }, root);
        Assert.True(plan.TryGet(P("A"), out var a));
        Assert.True(a!.IsStructuralParent);
        Assert.Equal(Path.Combine(root, "A.sbd"), a.SbdDirPath);
    }

    [Fact]
    public void Plan_CollidingSanitizedNames_GetDeterministicSuffix_AndWarn()
    {
        // "Team/Alpha" and "Team Alpha" both sanitize to "Team Alpha".
        var warnings = new List<string>();
        var plan = MboxTreePlanner.Plan(new[] { P("Team/Alpha"), P("Team Alpha") }, "r", warnings.Add);

        Assert.True(plan.TryGet(P("Team/Alpha"), out var first));
        Assert.True(plan.TryGet(P("Team Alpha"), out var second));
        Assert.Equal(Path.Combine("r", "Team Alpha"), first!.MboxFilePath);        // first wins
        Assert.Equal(Path.Combine("r", "Team Alpha (2)"), second!.MboxFilePath);   // second suffixed
        Assert.Contains(warnings, w => w.Contains("collides"));
    }

    [Fact]
    public void Plan_StructuralParentSbdVsLiteralSbdLeaf_DoNotClash()
    {
        // "X" is a structural parent -> file "X" + dir "X.sbd". A sibling leaf literally named "X.sbd"
        // would sanitize to "X.sbd" and clash with that directory; the planner must avoid it + warn.
        var warnings = new List<string>();
        var plan = MboxTreePlanner.Plan(new[] { P("X", "Child"), P("X.sbd") }, "r", warnings.Add);

        Assert.True(plan.TryGet(P("X"), out var x));
        Assert.True(plan.TryGet(P("X.sbd"), out var leaf));
        Assert.True(x!.IsStructuralParent);
        Assert.NotEqual(x.SbdDirPath, leaf!.MboxFilePath);   // the .sbd dir and the leaf file must differ
        Assert.Contains(warnings, w => w.Contains("collides"));
    }

    [Fact]
    public void ResolveMboxPath_BuildsSbdPath_NoCollisionTracking()
        => Assert.Equal(
            Path.Combine("r", "A.sbd", "B"),
            MboxTreePlanner.ResolveMboxPath(P("A", "B"), "r"));
}

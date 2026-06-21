// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryColorApplierTests
{
    private sealed class FakeStore : IOutlookCategoryStore
    {
        private readonly HashSet<string> _existing;
        public readonly List<(string Name, int Color)> Added = new();
        public FakeStore(params string[] existing) =>
            _existing = new HashSet<string>(existing, System.StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ExistingNames() => _existing;
        public void Add(string name, int outlookColorIndex) { Added.Add((name, outlookColorIndex)); _existing.Add(name); }
    }

    [Fact]
    public void AddsNew_AndCarriesColor()
    {
        var plan = new List<CategoryCandidate> { new("Critique", "#FF0000", 1, "would-add") };
        var store = new FakeStore();
        var results = CategoryColorApplier.Apply(plan, store);
        Assert.Equal("added", results.Single().Action);
        Assert.Equal(("Critique", 1), store.Added.Single());
    }

    [Fact]
    public void SkipsExisting_CaseInsensitive_NoOverwrite()
    {
        var plan = new List<CategoryCandidate> { new("Critique", "#FF0000", 1, "would-add") };
        var store = new FakeStore("critique"); // different case already present
        var results = CategoryColorApplier.Apply(plan, store);
        Assert.Equal("skipped-existing", results.Single().Action);
        Assert.Empty(store.Added);
    }

    [Fact]
    public void PassesThroughSkippedCandidates_WithoutCallingStore()
    {
        var plan = new List<CategoryCandidate>
        {
            new("NoColour", null, null, "skipped-no-colour"),
            new("Foo, Bar", "#FF0000", null, "skipped-invalid-name"),
        };
        var store = new FakeStore();
        var results = CategoryColorApplier.Apply(plan, store);
        Assert.Equal(new[] { "skipped-no-colour", "skipped-invalid-name" }, results.Select(r => r.Action));
        Assert.Empty(store.Added);
    }
}

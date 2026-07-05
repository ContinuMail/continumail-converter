// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests.OutlookProfiles;

public class OutlookProfileResolverTests
{
    private static OutlookProfileInfo Info(string? def, params string[] profiles) => new(profiles, def);

    [Fact] public void Explicit_flag_wins() =>
        Assert.Equal("Colour", OutlookProfileResolver.Resolve("Colour", Info("Work", "Colour", "Work")).Name);

    [Fact] public void Explicit_unknown_is_error_not_fallback() =>
        Assert.Equal("unknown-outlook-profile", OutlookProfileResolver.Resolve("Nope", Info("Work", "Work")).ErrorCode);

    [Fact] public void Default_used_when_present_in_list() =>
        Assert.Equal("Work", OutlookProfileResolver.Resolve(null, Info("Work", "Colour", "Work")).Name);

    [Fact] public void Ghost_default_falls_through_to_sole() =>
        Assert.Equal("Colour", OutlookProfileResolver.Resolve(null, Info("Ghost", "Colour")).Name);

    [Fact] public void Ghost_default_multiple_profiles_is_ambiguous() =>
        Assert.Equal("ambiguous-outlook-profile", OutlookProfileResolver.Resolve(null, Info("Ghost", "A", "B")).ErrorCode);

    [Fact] public void No_profiles_is_no_outlook_profile() =>
        Assert.Equal("no-outlook-profile", OutlookProfileResolver.Resolve(null, Info(null)).ErrorCode);

    [Fact] public void Explicit_match_is_case_insensitive_but_returns_registry_casing() =>
        Assert.Equal("Colour", OutlookProfileResolver.Resolve("colour", Info(null, "Colour")).Name);
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Linq;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class PstValidatorRunnerTests
{
    private const string OkJson =
        "{\"schemaVersion\":1,\"opened\":true,\"file\":\"a.pst\"," +
        "\"folders\":[{\"path\":[\"Inbox\"],\"displayPath\":\"Inbox\",\"messageCount\":3}]," +
        "\"totalMessages\":3,\"errors\":[]}";

    private const string FailJson =
        "{\"schemaVersion\":1,\"opened\":false,\"file\":\"bad.pst\"," +
        "\"folders\":[],\"totalMessages\":0," +
        "\"errors\":[{\"stage\":\"open\",\"message\":\"boom\"}]}";

    [Fact]
    public void Interpret_ParsesValidSuccess()
    {
        var r = PstValidatorRunner.Interpret(OkJson, "", 0);
        Assert.True(r.Opened);
        Assert.Equal(3, r.TotalMessages);
        Assert.Equal(new[] { "Inbox" }, r.Folders.Single().Path);
        Assert.Equal(3, r.Folders.Single().MessageCount);
    }

    [Fact]
    public void Interpret_ReturnsFailure_WhenExitNonzeroAndJsonAgrees()
    {
        var r = PstValidatorRunner.Interpret(FailJson, "", 1);
        Assert.False(r.Opened);
        Assert.Equal("open", r.Errors.Single().Stage);
    }

    [Fact]
    public void Interpret_Throws_OnEmptyStdout() =>
        Assert.Throws<PstValidatorException>(() => PstValidatorRunner.Interpret("   ", "diag", 1));

    [Fact]
    public void Interpret_Throws_OnInvalidJson() =>
        Assert.Throws<PstValidatorException>(() => PstValidatorRunner.Interpret("not json", "", 0));

    [Fact]
    public void Interpret_Throws_OnMultipleJsonObjects() =>
        Assert.Throws<PstValidatorException>(() => PstValidatorRunner.Interpret(OkJson + OkJson, "", 0));

    [Fact]
    public void Interpret_Throws_WhenExitZeroButJsonFailure() =>
        Assert.Throws<PstValidatorException>(() => PstValidatorRunner.Interpret(FailJson, "", 0));

    [Fact]
    public void Interpret_Throws_WhenExitNonzeroButJsonSuccess() =>
        Assert.Throws<PstValidatorException>(() => PstValidatorRunner.Interpret(OkJson, "", 1));
}

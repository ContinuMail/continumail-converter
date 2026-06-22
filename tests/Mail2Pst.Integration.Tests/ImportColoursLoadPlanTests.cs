// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Cli;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class ImportColoursLoadPlanTests
{
    private static string WriteTempPlan(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mail2pst-loadplan-{Guid.NewGuid()}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void WouldAdd_WithNullColour_IsDowngradedToSkippedNoColour()
    {
        string path = WriteTempPlan("""
            [{"name":"Alpha","hex":null,"action":"would-add"}]
            """);
        try
        {
            var plan = ImportColoursCommand.LoadPlanFromFile(path);
            Assert.Single(plan);
            Assert.Equal("skipped-no-colour", plan[0].Action);
            Assert.Null(plan[0].OutlookColor);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ActionAbsent_WithColour_IsWouldAdd()
    {
        string path = WriteTempPlan("""
            [{"name":"Beta","hex":"#FF0000","outlookColor":6}]
            """);
        try
        {
            var plan = ImportColoursCommand.LoadPlanFromFile(path);
            Assert.Single(plan);
            Assert.Equal("would-add", plan[0].Action);
            Assert.Equal(6, plan[0].OutlookColor);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ActionAbsent_WithNullColour_IsSkippedNoColour()
    {
        string path = WriteTempPlan("""
            [{"name":"Gamma","hex":null}]
            """);
        try
        {
            var plan = ImportColoursCommand.LoadPlanFromFile(path);
            Assert.Single(plan);
            Assert.Equal("skipped-no-colour", plan[0].Action);
            Assert.Null(plan[0].OutlookColor);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ActionPresent_NonWouldAdd_IsPreservedAsIs()
    {
        string path = WriteTempPlan("""
            [{"name":"Delta","hex":"#00FF00","outlookColor":3,"action":"skipped-existing"}]
            """);
        try
        {
            var plan = ImportColoursCommand.LoadPlanFromFile(path);
            Assert.Single(plan);
            Assert.Equal("skipped-existing", plan[0].Action);
            Assert.Equal(3, plan[0].OutlookColor);
        }
        finally { File.Delete(path); }
    }
}

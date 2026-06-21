// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using Mail2Pst.Core.OutlookCategories;
using Xunit;

namespace Mail2Pst.Core.Tests.OutlookCategories;

public class CategoryListXmlTests
{
    // Shape mirrors the real IPM.Configuration.CategoryList stream: a default-namespaced root with categories
    // whose 'color' is the 0-based MS-OXOCFG index.
    private const string Sample =
        "<?xml version=\"1.0\"?>\r\n" +
        "<categories default=\"\" lastSavedSession=\"2\" lastSavedTime=\"2026-06-21T18:05:45.683\" xmlns=\"CategoryList.xsd\">\r\n" +
        "\t<category name=\"Red category\" color=\"0\" keyboardShortcut=\"0\" guid=\"{FCBD6427-68FB-40ED-A698-3D4A66062ECC}\"/>\r\n" +
        "\t<category name=\"Yellow category\" color=\"3\" keyboardShortcut=\"0\" guid=\"{A6983171-BCC5-4F6C-B8A3-DA06AE1B1A25}\"/>\r\n" +
        "</categories>";

    [Fact]
    public void ReadNames_ReturnsExisting_CaseInsensitive()
    {
        IReadOnlySet<string> names = CategoryListXml.ReadNames(Sample);
        Assert.Equal(2, names.Count);
        Assert.Contains("Red category", names);
        Assert.Contains("yellow CATEGORY", names); // OrdinalIgnoreCase
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public void ReadNames_EmptyOrWhitespace_ReturnsEmpty(string xml) =>
        Assert.Empty(CategoryListXml.ReadNames(xml));

    [Fact]
    public void ReadNames_Bom_IsTolerated()
    {
        IReadOnlySet<string> names = CategoryListXml.ReadNames('﻿' + Sample);
        Assert.Contains("Red category", names);
    }

    [Fact]
    public void ReadNames_Malformed_ThrowsFormatException() =>
        Assert.Throws<FormatException>(() => CategoryListXml.ReadNames("<categories><not closed"));

    [Fact]
    public void Append_AddsNodes_PreservesExisting_AndRoundTrips()
    {
        string result = CategoryListXml.Append(Sample, new[] { ("Work", 2), ("To Do", 8) });
        IReadOnlySet<string> names = CategoryListXml.ReadNames(result);
        Assert.Equal(new[] { "Red category", "To Do", "Work", "Yellow category" }, names.OrderBy(n => n).ToArray());
    }

    [Theory]
    [InlineData(1, "0")]   // Red
    [InlineData(2, "1")]   // Orange
    [InlineData(8, "7")]   // Blue
    [InlineData(25, "24")] // DarkMaroon
    public void Append_MapsOlCategoryColorToZeroBasedXmlColor(int outlookColor, string expectedXmlColor)
    {
        string result = CategoryListXml.Append(Sample, new[] { ("Foo", outlookColor) });
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(result);
        var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("c", "CategoryList.xsd");
        var node = doc.SelectSingleNode("//c:category[@name='Foo']", ns);
        Assert.Equal(expectedXmlColor, node!.Attributes!["color"]!.Value);
    }

    [Fact]
    public void Append_NewNode_HasGuidInBraceUpperForm_AndShortcutZero()
    {
        string result = CategoryListXml.Append(Sample, new[] { ("Foo", 2) });
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(result);
        var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("c", "CategoryList.xsd");
        var node = doc.SelectSingleNode("//c:category[@name='Foo']", ns)!;
        string guid = node.Attributes!["guid"]!.Value;
        Assert.Matches("^\\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\\}$", guid);
        Assert.Equal("0", node.Attributes!["keyboardShortcut"]!.Value);
    }

    [Fact]
    public void Append_EscapesSpecialChars_AndPreservesUnicode()
    {
        string result = CategoryListXml.Append(Sample, new[] { ("A & B <\"x\"> ÆØÅ", 4) });
        // Raw markup must be escaped, the unicode must survive verbatim, and it must round-trip to the literal.
        Assert.DoesNotContain("A & B", result);
        Assert.Contains("ÆØÅ", result);
        Assert.Contains("A & B <\"x\"> ÆØÅ", CategoryListXml.ReadNames(result));
    }

    [Fact]
    public void Append_NewNode_InheritsDefaultNamespace_NoEmptyXmlns()
    {
        string result = CategoryListXml.Append(Sample, new[] { ("Work", 2) });
        Assert.DoesNotContain("xmlns=\"\"", result); // a wrong namespace would emit this on the new node
    }

    [Fact]
    public void Append_DeclarationEncodingMatchesUtf8Bytes_AndIsBomFree()
    {
        // The caller writes the result as UTF-8 (no BOM); the declaration must agree, not contradict it.
        // utf-16 here would be the classic StringWriter bug that breaks Outlook's reader.
        string result = CategoryListXml.Append(Sample, new[] { ("Work", 2) });
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", result);
        Assert.DoesNotContain("utf-16", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('﻿', result);
    }

    [Fact]
    public void Append_EmptyInput_StartsFreshList()
    {
        string result = CategoryListXml.Append("", new[] { ("Work", 2) });
        Assert.Equal(new[] { "Work" }, CategoryListXml.ReadNames(result).ToArray());
        Assert.DoesNotContain("xmlns=\"\"", result);
    }

    [Fact]
    public void Append_NoAdditions_IsValidNoOpRoundTrip()
    {
        string result = CategoryListXml.Append(Sample, Array.Empty<(string, int)>());
        Assert.Equal(2, CategoryListXml.ReadNames(result).Count);
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Mail2Pst.Core.OutlookCategories;

/// <summary>
/// Pure string transforms over the Outlook master category list XML — the CategoryList FAI's
/// <c>PidTagRoamingXmlStream</c> (MS-OXOCFG §2.2.5.1.1). No COM/Outlook types, so it is fully unit-testable;
/// the CLI's late-bound COM shim handles only the binary read/write of that stream.
/// </summary>
public static class CategoryListXml
{
    private const string DefaultNamespace = "CategoryList.xsd";
    private const char Bom = '﻿';

    /// <summary>Category names present in the list, compared OrdinalIgnoreCase. Empty/whitespace input
    /// yields an empty set. Throws <see cref="FormatException"/> on malformed (non-empty) XML.</summary>
    public static IReadOnlySet<string> ReadNames(string xml)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        XmlElement? root = Load(xml).DocumentElement;
        if (root is null) return names;
        foreach (XmlNode node in root.ChildNodes)
            if (node is XmlElement el && el.LocalName == "category")
            {
                string name = el.GetAttribute("name");
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
        return names;
    }

    /// <summary>Appends one &lt;category&gt; node per addition (name + OlCategoryColor 1-25) and returns the
    /// new XML as a string with a BOM-free <c>&lt;?xml version="1.0"?&gt;</c> declaration (the format Outlook
    /// expects). Empty/whitespace input starts a fresh list. Refreshes <c>lastSavedTime</c>. Throws
    /// <see cref="FormatException"/> on malformed (non-empty) XML.</summary>
    public static string Append(string xml, IReadOnlyList<(string Name, int OutlookColor)> additions)
    {
        ArgumentNullException.ThrowIfNull(additions);
        XmlDocument doc = Load(xml);
        XmlElement root = doc.DocumentElement!;
        string ns = root.NamespaceURI;

        foreach ((string name, int outlookColor) in additions)
        {
            XmlElement cat = doc.CreateElement("category", ns);
            cat.SetAttribute("name", name); // SetAttribute escapes &, <, ", and any unicode safely
            // The XML 'color' is the 0-based MS-OXOCFG index; OlCategoryColor (1-25) is that index + 1.
            cat.SetAttribute("color", (outlookColor - 1).ToString(CultureInfo.InvariantCulture));
            cat.SetAttribute("keyboardShortcut", "0");
            cat.SetAttribute("usageCount", "0");
            cat.SetAttribute("guid", Guid.NewGuid().ToString("B").ToUpperInvariant()); // {XXXXXXXX-....}
            root.AppendChild(cat);
        }
        root.SetAttribute("lastSavedTime",
            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
        return Serialize(doc);
    }

    // Loads the XML, tolerating a leading UTF-8 BOM / surrounding whitespace, and synthesizing a fresh
    // <categories> root for empty input. Wraps parse failures in FormatException so callers report a clear,
    // actionable error rather than a raw XmlException.
    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        string trimmed = (xml ?? string.Empty).Trim(Bom, ' ', '\t', '\r', '\n');
        if (trimmed.Length == 0)
        {
            doc.AppendChild(doc.CreateXmlDeclaration("1.0", null, null));
            doc.AppendChild(doc.CreateElement("categories", DefaultNamespace));
            return doc;
        }
        try { doc.LoadXml(trimmed); }
        catch (XmlException ex) { throw new FormatException("The Outlook category list XML is malformed.", ex); }
        if (doc.DocumentElement is null)
            throw new FormatException("The Outlook category list XML has no root element.");
        return doc;
    }

    private static string Serialize(XmlDocument doc)
    {
        // The document carries an <?xml version="1.0"?> declaration with no encoding attribute, so emitting to
        // a UTF-8-reporting writer keeps the declaration encoding-free (no spurious utf-16/utf-8 attribute that
        // would contradict the UTF-8 bytes the caller writes to the stream).
        var settings = new XmlWriterSettings { OmitXmlDeclaration = false };
        var sw = new Utf8StringWriter();
        using (XmlWriter w = XmlWriter.Create(sw, settings)) doc.Save(w);
        return sw.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Discovery;
using Xunit;

namespace Mail2Pst.Core.Tests.Discovery;

public class MailTreeDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m2p-disc-" + Guid.NewGuid());

    public MailTreeDiscoveryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string File_(string rel, string content)
    {
        string p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content, new UTF8Encoding(false));
        return p;
    }
    private void Dir_(string rel) => Directory.CreateDirectory(Path.Combine(_root, rel));

    private const string Msg = "From a@b Mon Jan  1 00:00:00 2020\r\nSubject: x\r\n\r\nbody\r\n";

    private static bool HasSource(DiscoveryResult r, params string[] path) =>
        r.Sources.Any(s => s.TargetFolderPath.SequenceEqual(path));

    [Fact]
    public void ParentFile_AndSiblingSbd_BothEmitted_WithNesting()
    {
        File_("Inbox", Msg);
        File_("Inbox.sbd/Acme", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Inbox"));
        Assert.True(HasSource(r, "Inbox", "Acme"));
        Assert.Equal("thunderbird", r.Layout);
    }

    [Fact]
    public void DeepNesting_StripsSbdSuffixPerLevel()
    {
        File_("Inbox.sbd/Acme.sbd/2024", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Inbox", "Acme", "2024"));
    }

    [Fact]
    public void OrphanSbd_EmitsChildrenButNoParentSource()
    {
        File_("Projects.sbd/Acme", Msg);   // no "Projects" file
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Projects", "Acme"));
        Assert.False(HasSource(r, "Projects"));
    }

    [Fact]
    public void MboxExtension_Stripped_ExtensionlessKept()
    {
        File_("WithExt.mbox", Msg);
        File_("NoExt", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "WithExt"));
        Assert.True(HasSource(r, "NoExt"));
    }

    [Fact]
    public void ZeroByteFile_IsEmptyFolderSource()
    {
        File_("Empty", "");
        var r = MailTreeDiscovery.Discover(_root);
        DiscoveredSource s = Assert.Single(r.Sources);
        Assert.Equal(0, s.SourceBytes);
        Assert.Equal(new[] { "Empty" }, s.TargetFolderPath);
    }

    [Fact]
    public void WhitespaceOnlyFile_IsNotMbox()
    {
        File_("Blank", "\r\n   \r\n\r\n");
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Empty(r.Sources);
        Assert.Contains(r.Skipped, s => s.Code == "not-mbox");
        Assert.Contains(r.Warnings, w => w.Code == "not-mbox");
    }

    [Fact]
    public void NonMboxFile_SkippedAndWarned()
    {
        File_("notmail.txt", "just some text\r\nmore text\r\n");
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Empty(r.Sources);
        Assert.Contains(r.Skipped, s => s.Code == "not-mbox");
    }

    [Fact]
    public void MetadataFiles_SummarizedAsOneSkip_NoWarning()
    {
        File_("Inbox", Msg);
        File_("Inbox.msf", "index");
        File_("global-messages-db.sqlite", "db");
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Inbox"));
        Assert.Single(r.Skipped, s => s.Code == "metadata-files-skipped");
        Assert.DoesNotContain(r.Warnings, w => w.Code.StartsWith("metadata"));
    }

    [Fact]
    public void UnexpectedSubdirectory_SkippedAndWarned()
    {
        Dir_("randomdir");
        File_("randomdir/whatever", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Contains(r.Skipped, s => s.Code == "unexpected-subdirectory");
        Assert.False(HasSource(r, "whatever"));
    }

    [Fact]
    public void InvalidSegmentFromSbdDir_SourceSanitized_PlusWarning()
    {
        File_("CON.sbd/Child", Msg);   // "CON" is a reserved Windows name
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Folder", "Child"));   // reserved name coerced via the fallback
        Assert.False(HasSource(r, "CON", "Child"));
        Assert.Contains(r.Warnings, w => w.Code == "invalid-folder-name" && w.Segment == "CON" && w.SegmentIndex == 0);
    }

    [Fact]
    public void DuplicateTargetPath_BothEmitted_PlusWarningWithRelatedPaths()
    {
        File_("Foo", Msg);
        File_("Foo.mbox", Msg);   // both strip to ["Foo"]
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Equal(2, r.Sources.Count(s => s.TargetFolderPath.SequenceEqual(new[] { "Foo" })));
        DiscoveryWarning w = Assert.Single(r.Warnings, x => x.Code == "duplicate-target-folder-path");
        Assert.Equal(2, w.RelatedPaths!.Count);
    }

    [Fact]
    public void NonAsciiNames_PreservedVerbatim()
    {
        File_("Føniks computer", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Føniks computer"));
    }

    [Fact]
    public void BomAndLeadingBlankThenPostmark_Accepted()
    {
        // UTF-8 BOM + blank line + valid From_ line
        string p = Path.Combine(_root, "Bommed");
        File.WriteAllText(p, "\r\n" + Msg, new UTF8Encoding(true)); // encoderShouldEmitUTF8Identifier: true => BOM
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Bommed"));
    }

    [Fact]
    public void Sources_AreDeterministicallyOrdered_ByPath()
    {
        File_("b", Msg); File_("a", Msg); File_("c", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        var paths = r.Sources.Select(s => s.Path).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(), paths);
    }

    [Fact]
    public void FlatMboxDir_LayoutFlatMbox()
    {
        File_("Inbox.mbox", Msg);
        File_("Sent.mbox", Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Equal("flat-mbox", r.Layout);
    }

    [Fact]
    public void EmptyDir_LayoutEmpty_NoSources()
    {
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Empty(r.Sources);
        Assert.Equal("empty", r.Layout);
    }

    [Fact]
    public void MixedLayout_ExtensionlessRootFileNoSbd_ReturnsMixed()
    {
        File_("Inbox", Msg);          // extensionless, no .sbd → not flat-mbox, not thunderbird
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Inbox"));
        Assert.Equal("mixed", r.Layout);
    }

    [Fact]
    public void HugeBlankPrefixThenPostmark_IsNotMbox()
    {
        // > 4 KB of blank lines before a valid From_ line: the sniff reads only the prefix, so it
        // must NOT find the postmark and must classify as not-mbox (pins the small-prefix rule).
        string blanks = new string('\n', 5000);
        File_("Bloated", blanks + Msg);
        var r = MailTreeDiscovery.Discover(_root);
        Assert.Empty(r.Sources);
        Assert.Contains(r.Skipped, s => s.Code == "not-mbox");
    }

    [SkippableFact]
    public void SymlinkNamedSbd_SkippedNotRecursed()
    {
        File_("Inbox.sbd/RealChild", Msg);   // a real .sbd tree to point the symlink at
        string link = Path.Combine(_root, "Link.sbd");
        try { Directory.CreateSymbolicLink(link, Path.Combine(_root, "Inbox.sbd")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Skip.If(true, "Cannot create a symlink/reparse-point in this environment: " + ex.Message);
            return;
        }

        var r = MailTreeDiscovery.Discover(_root);
        Assert.Contains(r.Skipped, s => s.Code == "symlink-skipped");
        // The link's target children must NOT be discovered via the link (no "Link" segment).
        Assert.DoesNotContain(r.Sources, s => s.TargetFolderPath.Count > 0 && s.TargetFolderPath[0] == "Link");
    }

    // Leading spaces are the one PST-invalid folder-name form that is portably creatable on disk
    // (Windows preserves leading — not trailing — spaces; Linux preserves both). FolderNameValidator
    // rejects leading/trailing whitespace, so this exercises the sanitize path on any CI OS.
    [Fact]
    public void LeadingSpaceLeafFolderName_IsSanitized_AndWarned_ButDisplayNameStaysRaw()
    {
        File_(" To Read", Msg);   // leaf " To Read" -> "To Read"
        var r = MailTreeDiscovery.Discover(_root);
        DiscoveredSource s = Assert.Single(r.Sources);
        Assert.Equal(new[] { "To Read" }, s.TargetFolderPath.ToArray());   // trimmed, valid path
        Assert.Equal(" To Read", s.DisplayName);                          // raw name kept for the UI
        Assert.Contains(r.Warnings, w => w.Code == "invalid-folder-name");
    }

    [Fact]
    public void LeadingSpaceSbdParentSegment_IsSanitized_InChildPath_AndWarned()
    {
        File_(" Parent.sbd/Child", Msg);   // parent segment " Parent" -> "Parent"
        var r = MailTreeDiscovery.Discover(_root);
        Assert.True(HasSource(r, "Parent", "Child"));
        Assert.Contains(r.Warnings, w => w.Code == "invalid-folder-name");
    }
}

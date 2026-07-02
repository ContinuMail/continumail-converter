// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Calendar;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class CalendarAttachmentResolverTests
{
    private static RawSideText Line(string s) => new(s);

    [Fact] public void Inline_data_uri_is_inline_bytes()
    {
        var r = new CalendarAttachmentResolver(exportRoot: null);
        var (atts, warns) = r.ResolveAll(new[] {
            Line("ATTACH;FILENAME=hi.txt;VALUE=BINARY;ENCODING=BASE64;FMTTYPE=text/plain:aGk=") }, "evt");
        var a = Assert.Single(atts);
        Assert.Equal(CalendarAttachmentKind.InlineBytes, a.Kind);
        Assert.Equal("hi.txt", a.FileName);
        Assert.Empty(warns);
    }

    [Fact] public void Remote_url_is_link_only_with_warning_never_fetched()
    {
        var r = new CalendarAttachmentResolver(exportRoot: null);
        var (atts, warns) = r.ResolveAll(new[] {
            Line("ATTACH;FILENAME=doc.pdf:https://drive.example.com/doc.pdf") }, "evt");
        var a = Assert.Single(atts);
        Assert.Equal(CalendarAttachmentKind.LinkOnly, a.Kind);
        Assert.Equal("https://drive.example.com/doc.pdf", a.PreservedReference);
        Assert.Contains(warns, w => w.Contains("preserved as link") && w.Contains("not fetched"));
    }

    // Build file: URIs with new Uri(path).AbsoluteUri (correct across Windows/Linux, spaces, drive letters).
    private static string FileUri(string path) => new Uri(path).AbsoluteUri;

    [Fact] public void Local_file_outside_root_is_link_only_with_warning()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        string outside = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.bin");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(outside, new byte[] { 1 });
        try
        {
            var r = new CalendarAttachmentResolver(root);
            var (atts, warns) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=x.bin:{FileUri(outside)}") }, "evt");
            Assert.Equal(CalendarAttachmentKind.LinkOnly, Assert.Single(atts).Kind);
            Assert.Contains(warns, w => w.Contains("outside export root"));
        }
        finally { Directory.Delete(root, true); File.Delete(outside); }
    }

    [Fact] public void Sensitive_profile_file_is_never_embedded()
    {
        // A file:// ATTACH pointing at a Thunderbird credential/key file inside the export root must NOT
        // be embedded (defense-in-depth against a crafted calendar exfiltrating secrets), even though it
        // passes the in-root check. Degrades to link-only with a warning.
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string secret = Path.Combine(root, "logins.json");
        File.WriteAllText(secret, "{\"secret\":true}");
        try
        {
            var r = new CalendarAttachmentResolver(root);
            var (atts, warns) = r.ResolveAll(new[] { Line($"ATTACH:{FileUri(secret)}") }, "evt");
            Assert.Equal(CalendarAttachmentKind.LinkOnly, Assert.Single(atts).Kind);
            Assert.Contains(warns, w => w.Contains("sensitive"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void Oversized_inline_is_dropped_with_accurate_warning()
    {
        // maxEmbedBytes small so a 100-byte inline attachment exceeds it. Inline data has no external
        // reference, so the warning must say it was DROPPED, not "preserved as link".
        var r = new CalendarAttachmentResolver(exportRoot: null, maxEmbedBytes: 8);
        string big = Convert.ToBase64String(new byte[100]);
        var (atts, warns) = r.ResolveAll(new[] {
            Line($"ATTACH;FILENAME=big.bin;VALUE=BINARY;ENCODING=BASE64;FMTTYPE=application/octet-stream:{big}") }, "evt");
        var a = Assert.Single(atts);
        Assert.Equal(CalendarAttachmentKind.LinkOnly, a.Kind);
        Assert.Contains(warns, w => w.Contains("dropped"));
        Assert.DoesNotContain(warns, w => w.Contains("preserved as link"));
    }

    [Fact] public void Local_file_missing_is_link_only_with_warning()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var r = new CalendarAttachmentResolver(root);
            string missing = Path.Combine(root, "gone.bin");
            var (atts, warns) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=gone.bin:{FileUri(missing)}") }, "evt");
            Assert.Equal(CalendarAttachmentKind.LinkOnly, Assert.Single(atts).Kind);
            Assert.Contains(warns, w => w.Contains("missing") || w.Contains("unreadable"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void Local_file_inside_root_is_by_value()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string f = Path.Combine(root, "ok.bin");
            File.WriteAllBytes(f, new byte[] { 9, 9 });
            var r = new CalendarAttachmentResolver(root);
            var (atts, _) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=ok.bin:{FileUri(f)}") }, "evt");
            var a = Assert.Single(atts);
            Assert.Equal(CalendarAttachmentKind.LocalFileByValue, a.Kind);
            Assert.Equal(Path.GetFullPath(f), a.LocalPath);
        }
        finally { Directory.Delete(root, true); }
    }

    // --- filename fallback (no FILENAME param) ---
    // Thunderbird's storage calendar keeps only the attachment URI (no FILENAME param),
    // so an embedded local file must take its name from the URI basename — a generic
    // extensionless "attachment" won't open cleanly from Outlook.

    [Fact] public void Local_file_without_filename_param_uses_uri_basename()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string f = Path.Combine(root, "pr8-note.txt");
            File.WriteAllBytes(f, new byte[] { 9 });
            var r = new CalendarAttachmentResolver(root);
            var (atts, _) = r.ResolveAll(new[] { Line($"ATTACH;FMTTYPE=text/plain:{FileUri(f)}") }, "evt");
            var a = Assert.Single(atts);
            Assert.Equal(CalendarAttachmentKind.LocalFileByValue, a.Kind);
            Assert.Equal("pr8-note.txt", a.FileName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void Local_file_filename_param_wins_over_uri_basename()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string f = Path.Combine(root, "on-disk.bin");
            File.WriteAllBytes(f, new byte[] { 9 });
            var r = new CalendarAttachmentResolver(root);
            var (atts, _) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=custom.txt:{FileUri(f)}") }, "evt");
            var a = Assert.Single(atts);
            Assert.Equal(CalendarAttachmentKind.LocalFileByValue, a.Kind);
            Assert.Equal("custom.txt", a.FileName);
        }
        finally { Directory.Delete(root, true); }
    }

    // --- size-guard tests ---

    [Fact] public void Local_file_exceeding_maxEmbedBytes_is_link_only_with_too_large_warning()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string f = Path.Combine(root, "big.bin");
            File.WriteAllBytes(f, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }); // 10 bytes
            var r = new CalendarAttachmentResolver(root, maxEmbedBytes: 4);        // threshold = 4
            var (atts, warns) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=big.bin:{FileUri(f)}") }, "evt");
            Assert.Equal(CalendarAttachmentKind.LinkOnly, Assert.Single(atts).Kind);
            Assert.Contains(warns, w => w.Contains("too large"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact] public void Inline_data_exceeding_maxEmbedBytes_is_link_only_with_too_large_warning()
    {
        // "hello" = 5 bytes when decoded; maxEmbedBytes = 4 forces LinkOnly
        var r = new CalendarAttachmentResolver(exportRoot: null, maxEmbedBytes: 4);
        var (atts, warns) = r.ResolveAll(new[] {
            Line("ATTACH;FILENAME=big.txt;VALUE=BINARY;ENCODING=BASE64;FMTTYPE=text/plain:aGVsbG8=") }, "evt");
        var a = Assert.Single(atts);
        Assert.Equal(CalendarAttachmentKind.LinkOnly, a.Kind);
        Assert.Contains(warns, w => w.Contains("too large"));
    }

    [Fact] public void Default_threshold_small_inline_still_classifies_as_inline_bytes()
    {
        // Regression: default maxEmbedBytes = int.MaxValue must not affect normal small data
        var r = new CalendarAttachmentResolver(exportRoot: null);
        var (atts, warns) = r.ResolveAll(new[] {
            Line("ATTACH;FILENAME=hi.txt;VALUE=BINARY;ENCODING=BASE64;FMTTYPE=text/plain:aGk=") }, "evt");
        var a = Assert.Single(atts);
        Assert.Equal(CalendarAttachmentKind.InlineBytes, a.Kind);
        Assert.Empty(warns);
    }

    [Fact] public void Default_threshold_small_local_file_still_classifies_as_by_value()
    {
        // Regression: default maxEmbedBytes = int.MaxValue must not affect normal small files
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string f = Path.Combine(root, "ok.bin");
            File.WriteAllBytes(f, new byte[] { 9, 9 });
            var r = new CalendarAttachmentResolver(root);
            var (atts, _) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=ok.bin:{FileUri(f)}") }, "evt");
            Assert.Equal(CalendarAttachmentKind.LocalFileByValue, Assert.Single(atts).Kind);
        }
        finally { Directory.Delete(root, true); }
    }

    [SkippableFact]   // symlink creation needs privilege on Windows; skip if it throws
    public void Local_file_symlink_inside_root_is_rejected()
    {
        string root = Path.Combine(Path.GetTempPath(), $"root-{Guid.NewGuid():N}");
        string target = Path.Combine(Path.GetTempPath(), $"target-{Guid.NewGuid():N}.bin");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(target, new byte[] { 5 });
        string link = Path.Combine(root, "link.bin");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) { Directory.Delete(root, true); File.Delete(target); throw new SkipException(ex.Message); }
        try
        {
            var r = new CalendarAttachmentResolver(root);
            var (atts, warns) = r.ResolveAll(new[] { Line($"ATTACH;FILENAME=link.bin:{FileUri(link)}") }, "evt");
            Assert.Equal(CalendarAttachmentKind.LinkOnly, Assert.Single(atts).Kind);
            Assert.Contains(warns, w => w.Contains("symlink") || w.Contains("reparse"));
        }
        finally { Directory.Delete(root, true); File.Delete(target); }
    }
}

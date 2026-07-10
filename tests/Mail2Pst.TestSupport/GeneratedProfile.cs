// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
namespace Mail2Pst.TestSupport;

/// <summary>Per-folder generation truth: what was written where.
/// TruncatedTailCount counts a deliberately-truncated trailing message (a corruption knob
/// added by a later, stacked task); it defaults to 0 for a clean profile, so the "written"
/// and "attempted" truth models agree when nothing is truncated.</summary>
public sealed record GeneratedFolder(string Name, string FilePath, int MessageCount, int TruncatedTailCount = 0);

/// <summary>A generated synthetic Thunderbird profile. Disposing deletes the whole
/// temp tree (including the deep-root prefix directories above the profile dir).</summary>
public sealed class GeneratedProfile : IDisposable
{
    /// <summary>The profile directory (contains prefs.js).</summary>
    public string RootPath { get; }

    /// <summary>Per-folder truth (name, file path, message count), in creation order.</summary>
    public IReadOnlyList<GeneratedFolder> Folders { get; }

    /// <summary>Convenience projection: absolute path of every generated mbox folder file.</summary>
    public IReadOnlyList<string> MailFilePaths { get; }

    private readonly string _deleteRoot;

    internal GeneratedProfile(string rootPath, string deleteRoot, IReadOnlyList<GeneratedFolder> folders)
    {
        RootPath = rootPath;
        _deleteRoot = deleteRoot;
        Folders = folders;
        MailFilePaths = folders.Select(f => f.FilePath).ToList();
    }

    public void Dispose()
    {
        try { Directory.Delete(_deleteRoot, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}

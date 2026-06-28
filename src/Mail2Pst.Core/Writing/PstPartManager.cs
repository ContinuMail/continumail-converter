// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;
using PSTFileFormat;

namespace Mail2Pst.Core.Writing;

/// <summary>
/// Owns the output-PST part lifecycle for a single WritePlan call: the current PSTFile
/// handle, part numbering, rename-on-first-split, the per-part folder cache, the per-part
/// size estimate/counters, and the predictive + checkpoint splits. Behaviour is a 1:1
/// extraction of logic previously inline in PstWriter.WritePlan. The write itself is
/// supplied as a delegate so this class never depends on PstWriter and PSTFile stays
/// encapsulated.
/// </summary>
internal sealed class PstPartManager
{
    private readonly string _groupName;
    private readonly string _outputDirectory;
    private readonly long _maxSizeBytes;
    private readonly int _checkIntervalMessages;
    private readonly Action<PSTFile, PSTFolder, MailMessage> _writeMessage;
    private readonly Action<PSTFile, PSTFolder, ContactRecord> _writeContact;
    private readonly long _emptyStoreSize;

    private readonly List<string> _outputFiles = new();
    private readonly Dictionary<string, PSTFolder> _folders = new();
    private readonly HashSet<PSTFolder> _dirtyFolders = new(ReferenceEqualityComparer.Instance);
    private PSTFile? _file;
    private string _currentPath = "";
    private int _partNumber = 1;
    private long _estimatedContentBytes;   // per-part; reset on split
    private int _messagesInCurrentPart;
    private int _messagesSinceCheck;

    public PstPartManager(string groupName, string outputDirectory,
        long maxSizeBytes, int checkIntervalMessages,
        Action<PSTFile, PSTFolder, MailMessage> writeMessage,
        Action<PSTFile, PSTFolder, ContactRecord> writeContact)
    {
        _groupName = groupName;
        _outputDirectory = outputDirectory;
        _maxSizeBytes = maxSizeBytes;
        _checkIntervalMessages = checkIntervalMessages;
        _writeMessage = writeMessage;
        _writeContact = writeContact;
        // From-scratch creation (PSTFile.CreateEmptyStore) seeds every part; the template
        // copy is retired. The initial on-disk size is the constant empty-store size.
        _emptyStoreSize = PSTFile.EmptyStoreSizeBytes;
    }

    public IReadOnlyList<string> OutputFiles => _outputFiles;
    public string CurrentPath => _currentPath;
    public bool CheckpointDue => _messagesSinceCheck >= _checkIntervalMessages;

    /// <summary>
    /// Opens the first (un-suffixed) part, begins saving, and pre-creates the given folders
    /// (the IncludeEmptyFolders set). Call exactly once, before the message loop.
    /// </summary>
    public void Begin(IReadOnlyList<IReadOnlyList<string>> foldersToPrecreate) =>
        Begin(foldersToPrecreate, Array.Empty<IReadOnlyList<string>>());

    /// <summary>
    /// Opens the first (un-suffixed) part, begins saving, and pre-creates the given mail and
    /// contact folders with their correct item types. Call exactly once, before the message loop.
    /// </summary>
    public void Begin(IReadOnlyList<IReadOnlyList<string>> mailFolders,
                      IReadOnlyList<IReadOnlyList<string>> contactFolders)
    {
        _currentPath = StartNewFile(_groupName, null, _outputDirectory);
        _outputFiles.Add(_currentPath);
        // Outlook2007RTM is load-bearing: it matches the compatibility mode CreateEmptyStore writes
        // (fAMapValid=VALID_AMAP2) and that the independent reader + Outlook validate against. A
        // mismatch here corrupts the AMap marker; the IndependentValidationTests gate would catch it.
        _file = new PSTFile(_currentPath, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        _file.BeginSavingChanges();
        foreach (IReadOnlyList<string> path in mailFolders)
            GetOrCreateFolder(path, FolderItemTypeName.Note);
        foreach (IReadOnlyList<string> path in contactFolders)
            GetOrCreateFolder(path, FolderItemTypeName.Contact);
    }

    public bool ShouldSplitBefore(long messageSize) =>
        _messagesInCurrentPart > 0 &&
        _emptyStoreSize + _estimatedContentBytes + messageSize >= _maxSizeBytes;

    /// <summary>Predictive split: flushes the current part, then starts the next.</summary>
    public void FlushAndSplit()
    {
        SaveDirtyFolders();
        _file!.EndSavingChanges();
        StartNextPartAfterFlush();
    }

    public void Write(IReadOnlyList<string> path, MailMessage message)
    {
        PSTFolder folder = GetOrCreateFolder(path, FolderItemTypeName.Note);
        _writeMessage(_file!, folder, message);
        _dirtyFolders.Add(folder);
    }

    public void WriteContact(IReadOnlyList<string> path, ContactRecord contact)
    {
        PSTFolder folder = GetOrCreateFolder(path, FolderItemTypeName.Contact);
        _writeContact(_file!, folder, contact);
        _dirtyFolders.Add(folder);
    }

    public void OnWritten(long messageSize)
    {
        _estimatedContentBytes += messageSize;
        _messagesInCurrentPart++;
        _messagesSinceCheck++;
    }

    // Flush every folder that received a message since the last save. The vendored library
    // requires folder.SaveChanges() before file.EndSavingChanges() or the contents-table
    // update is lost — batching the per-folder save to each flush boundary (instead of per
    // message) avoids rewriting a folder's whole contents table on every single message.
    // An empty dirty set is a no-op (empty PSTs, pre-created-only folders, finish-with-no-writes).
    private void SaveDirtyFolders()
    {
        foreach (PSTFolder folder in _dirtyFolders)
            folder.SaveChanges();
        _dirtyFolders.Clear();
    }

    /// <summary>Saves dirty folders, then EndSavingChanges — the checkpoint path flushes here so progress can emit before the size decision.</summary>
    public void Flush()
    {
        SaveDirtyFolders();
        _file!.EndSavingChanges();
    }

    /// <summary>
    /// PRECONDITION: Flush() was just called. Splits if the part reached the cap (returns
    /// true), else resumes saving (returns false). POSTCONDITION (both branches): the part
    /// is in an active BeginSavingChanges state, ready for more writes or Finish().
    /// </summary>
    public bool TrySplitOrResumeAfterFlush()
    {
        long size = Math.Max(_file!.BaseStream.Length, _emptyStoreSize + _estimatedContentBytes);
        if (size >= _maxSizeBytes)
        {
            StartNextPartAfterFlush();   // opens the next part with BeginSavingChanges active
            return true;
        }
        _messagesSinceCheck = 0;
        _file.BeginSavingChanges();
        return false;
    }

    public void Finish()
    {
        SaveDirtyFolders();
        _file!.EndSavingChanges();
    }

    /// <summary>Closes the current handle. Idempotent: nulls the field so a double Close() is a no-op.</summary>
    public void Close()
    {
        _file?.CloseFile();
        _file = null;
    }

    public void DeleteCurrentPart() => TryDeletePart(_currentPath);

    /// <summary>Measurement-only [§7]: aggregate this part's durable-memory residency from live state.</summary>
    internal Mail2Pst.Core.Diagnostics.DurableMemoryReport SnapshotDurableMemory(int messagesWritten)
    {
        if (_file is null) return new Mail2Pst.Core.Diagnostics.DurableMemoryReport(
            System.Array.Empty<Mail2Pst.Core.Diagnostics.FamilyResidency>(), 0, messagesWritten);
        return Mail2Pst.Core.Diagnostics.DurableMemoryCollector.Collect(_file, _folders.Values, messagesWritten);
    }

    // Closes the current (already-flushed) part and opens the next. PRECONDITION: the
    // caller has just flushed (EndSavingChanges). Once the old part is closed it is COMPLETE,
    // so _currentPath is cleared until the next part opens: if creating the next part throws,
    // the fatal-cleanup DeleteCurrentPart() must delete neither the completed part nor a
    // half-made next part (the orphan template copy is removed here). Builds the next part
    // fully into LOCALS before mutating shared state, so a StartNewFile/PSTFile failure leaves
    // the last-good part intact rather than half-initialised.
    private void StartNextPartAfterFlush()
    {
        _file!.CloseFile();
        _file = null;

        // The just-closed part is complete. There is no in-progress part until the next one
        // opens; clear _currentPath so an abort mid-split targets nothing (see DeleteCurrentPart).
        string completedPath = _currentPath;
        _currentPath = "";

        if (_partNumber == 1)
        {
            // First split: the initial "Name.pst" becomes part 1.
            string part1 = ResolveOutputPath(_groupName, 1, _outputDirectory);
            try
            {
                TransientFileRetry.Run(() => File.Move(completedPath, part1, overwrite: true));
            }
            catch (IOException ex)
            {
                // Any IOException that escapes the retry helper — a transient violation whose
                // retries were exhausted, or a non-transient one surfaced immediately — gets a
                // clear, attributable message instead of a bare File.Move failure. The initial
                // part still exists under its original name (the move failed) and is kept.
                throw new IOException(
                    $"Failed to rename '{completedPath}' to '{part1}' while starting split part 2.", ex);
            }
            _outputFiles[0] = part1;
        }

        int nextPartNumber = _partNumber + 1;
        string nextPath = "";
        PSTFile? nextFile = null;
        bool opened = false;
        try
        {
            nextPath = StartNewFile(_groupName, nextPartNumber, _outputDirectory);
            // same Outlook2007RTM contract as Begin()
            nextFile = new PSTFile(nextPath, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            nextFile.BeginSavingChanges();

            // New part open and ready — only now mutate shared state.
            _partNumber = nextPartNumber;
            _currentPath = nextPath;
            _outputFiles.Add(nextPath);
            _file = nextFile;
            _folders.Clear();
            _dirtyFolders.Clear();
            _estimatedContentBytes = 0;
            _messagesSinceCheck = 0;
            _messagesInCurrentPart = 0;
            opened = true;
        }
        finally
        {
            if (!opened)
            {
                // The split failed after the previous part was completed. Close the orphan
                // handle and delete the orphan template copy (if StartNewFile got that far) so
                // a failed split leaves no stray blank part. _currentPath stays "" so the
                // completed parts already on disk are preserved by the caller's fatal cleanup.
                nextFile?.CloseFile();
                if (nextPath.Length > 0) TryDeletePart(nextPath);
            }
        }
    }

    private string StartNewFile(string groupName, int? partNumber, string outputDirectory)
    {
        string fullPath = ResolveOutputPath(groupName, partNumber, outputDirectory);
        TransientFileRetry.Run(() => PSTFile.CreateEmptyStore(fullPath));
        return fullPath;
    }

    private static string ResolveOutputPath(string groupName, int? partNumber, string outputDirectory)
    {
        OutputNameValidator.Validate(groupName);
        string fileName = partNumber is null ? $"{groupName}.pst" : $"{groupName}-{partNumber}.pst";
        string fullDir = Path.GetFullPath(outputDirectory);
        string fullPath = Path.GetFullPath(Path.Combine(fullDir, fileName));
        string relative = Path.GetRelativePath(fullDir, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new ConfigValidationException($"Resolved output path escapes the output directory: {fullPath}");
        return fullPath;
    }

    private PSTFolder GetOrCreateFolder(IReadOnlyList<string> path, FolderItemTypeName itemType)
    {
        if (path.Count == 0)
            throw new InvalidOperationException("GetOrCreateFolder requires a non-empty path.");

        PSTFolder current = _file!.TopOfPersonalFolders;
        var prefix = new List<string>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            string segment = path[i];
            prefix.Add(segment);
            string key = FolderPathKey.Join(prefix);
            bool isLeaf = i == path.Count - 1;
            // Leaf carries the requested item type; parents are containers (Note default is fine).
            FolderItemTypeName segType = isLeaf ? itemType : FolderItemTypeName.Note;

            if (_folders.TryGetValue(key, out PSTFolder? cached))
            {
                GuardLeafClass(isLeaf, segType, cached, key);
                current = cached; continue;
            }
            PSTFolder? existing = current.FindChildFolder(segment);
            if (existing != null) GuardLeafClass(isLeaf, segType, existing, key);
            current = existing ?? current.CreateChildFolder(segment, segType);
            _folders[key] = current; // cache EVERY level, not just the leaf
        }
        return current;
    }

    // Two-way guard: a reused leaf's container class must match the requested item type
    // (mail asks for IPF.Note, contacts for IPF.Contact). Non-leaf segments are containers
    // and are not type-checked.
    private static void GuardLeafClass(bool isLeaf, FolderItemTypeName segType, PSTFolder folder, string key)
    {
        if (!isLeaf) return;
        string expected = PSTFolder.GetContainerClass(segType);   // e.g. "IPF.Contact" / "IPF.Note"
        if (!string.Equals(folder.ContainerClass, expected, StringComparison.Ordinal))
            throw new ConfigValidationException(
                $"Folder '{key}' already exists as '{folder.ContainerClass}'; cannot reuse it as '{expected}'.");
    }

    private static void TryDeletePart(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using PSTFileFormat;
using Xunit;

namespace Mail2Pst.Core.Tests.PSTFileFormat;

// Direct validity tests for the from-scratch empty store (PSTFile.CreateEmptyStore). These replace
// the retired template-asset provenance/provider tests: they prove the GENERATED seed is valid and
// stable, not that a checked-in asset is byte-unchanged.
public class CreateEmptyStoreTests
{
    private static string TempPstPath() =>
        Path.Combine(Path.GetTempPath(), $"m2p-empty-{Guid.NewGuid():N}.pst");

    [Fact]
    public void CreateEmptyStore_WritesNonEmptyFile()
    {
        string path = TempPstPath();
        try
        {
            global::PSTFileFormat.PSTFile.CreateEmptyStore(path);
            Assert.True(File.Exists(path));
            Assert.Equal(global::PSTFileFormat.PSTFile.EmptyStoreSizeBytes, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CreateEmptyStore_OpensWithVendoredReader_AndHasDefaultFolders()
    {
        string path = TempPstPath();
        try
        {
            global::PSTFileFormat.PSTFile.CreateEmptyStore(path);
            var file = new global::PSTFileFormat.PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                Assert.NotNull(file.TopOfPersonalFolders);
                // English default-folder naming is intentional (from the deterministic blueprint);
                // the old Danish "Slettet post" is gone.
                Assert.NotNull(file.TopOfPersonalFolders.FindChildFolder("Deleted Items"));
            }
            finally { file.CloseFile(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CreateEmptyStore_OverwritesExistingFile()
    {
        string path = TempPstPath();
        try
        {
            File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }); // not a PST
            global::PSTFileFormat.PSTFile.CreateEmptyStore(path);            // must overwrite, not throw
            var file = new global::PSTFileFormat.PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try { Assert.NotNull(file.TopOfPersonalFolders); }
            finally { file.CloseFile(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CreateEmptyStore_AcceptsFolderAndMessage_SurvivesReopen()
    {
        string path = TempPstPath();
        try
        {
            global::PSTFileFormat.PSTFile.CreateEmptyStore(path);

            var file = new global::PSTFileFormat.PSTFile(path, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                file.BeginSavingChanges();
                PSTFolder folder = file.TopOfPersonalFolders.CreateChildFolder("Inbox", FolderItemTypeName.Note);
                Note note = Note.CreateNewNote(file, folder.NodeID);
                note.Subject = "hello";
                note.SaveChanges();
                folder.AddMessage(note);
                folder.SaveChanges();      // required before EndSavingChanges or the contents-table update is lost
                file.EndSavingChanges();
            }
            finally { file.CloseFile(); }

            var reopened = new global::PSTFileFormat.PSTFile(path, FileAccess.Read, WriterCompatibilityMode.Outlook2007RTM);
            try
            {
                PSTFolder inbox = reopened.TopOfPersonalFolders.FindChildFolder("Inbox");
                Assert.NotNull(inbox);
                Assert.Equal(1, inbox.MessageCount);
            }
            finally { reopened.CloseFile(); }
        }
        finally { File.Delete(path); }
    }
}

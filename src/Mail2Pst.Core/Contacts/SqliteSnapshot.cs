// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Mail2Pst.Core.Contacts;

/// <summary>
/// Opens a Thunderbird SQLite address book for read. Tries a read-only connection on the
/// original; if that throws (locked), copies the DB + -wal/-shm sidecars to a scratch dir
/// and opens the copy. Owns and deletes the scratch dir on Dispose, including failure paths.
/// Callers must fully materialize results before disposing (no lazy enumeration past Dispose).
/// </summary>
public sealed class SqliteSnapshot : IDisposable
{
    public SqliteConnection Connection { get; private set; }
    public string? ScratchDirectory { get; private set; }

    private SqliteSnapshot(SqliteConnection connection, string? scratchDir)
    {
        Connection = connection;
        ScratchDirectory = scratchDir;
    }

    public static SqliteSnapshot Open(string dbPath)
    {
        // 1) Try read-only on the original.
        try
        {
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            conn.Open();
            return new SqliteSnapshot(conn, null);
        }
        catch (SqliteException) { /* locked or busy — try backup API, then copy */ }

        // 2) SQLite online backup API into an in-scratch DB (consistent even if TB is writing).
        string scratch = Path.Combine(Path.GetTempPath(), $"m2p-abk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        string backupPath = Path.Combine(scratch, Path.GetFileName(dbPath));
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly,
                   }.ToString()))
            using (var dest = new SqliteConnection($"Data Source={backupPath}"))
            {
                source.Open();
                dest.Open();
                source.BackupDatabase(dest); // Microsoft.Data.Sqlite online backup
            }
            var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            conn.Open();
            return new SqliteSnapshot(conn, scratch);
        }
        catch (SqliteException)
        {
            // 3) Last resort: raw file copy of DB + -wal/-shm sidecars (best-effort, not consistent
            //    if TB is mid-write — acceptable fallback when the backup API itself failed).
            try
            {
                string copyPath = Path.Combine(scratch, Path.GetFileName(dbPath));
                File.Copy(dbPath, copyPath, overwrite: true);
                foreach (string suffix in new[] { "-wal", "-shm" })
                {
                    string side = dbPath + suffix;
                    if (File.Exists(side)) File.Copy(side, copyPath + suffix, overwrite: true);
                }
                var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = copyPath, Mode = SqliteOpenMode.ReadOnly,
                }.ToString());
                conn.Open();
                return new SqliteSnapshot(conn, scratch);
            }
            catch { TryDeleteDir(scratch); throw; }
        }
        catch { TryDeleteDir(scratch); throw; }
    }

    public void Dispose()
    {
        try { Connection.Dispose(); } catch { /* ignore */ }
        SqliteConnection.ClearAllPools();
        if (ScratchDirectory != null) TryDeleteDir(ScratchDirectory);
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

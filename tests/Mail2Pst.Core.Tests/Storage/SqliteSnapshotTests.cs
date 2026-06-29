// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using Mail2Pst.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Mail2Pst.Core.Tests.Storage;

public class SqliteSnapshotTests
{
    [Fact]
    public void Open_ReadsRows_AndCleansUpScratchOnDispose()
    {
        string db = Path.Combine(Path.GetTempPath(), $"m2p-sql-{Guid.NewGuid():N}.sqlite");
        using (var conn = new SqliteConnection($"Data Source={db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t(x TEXT); INSERT INTO t VALUES('hi');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        try
        {
            string? scratch;
            using (SqliteSnapshot snap = SqliteSnapshot.Open(db))
            {
                scratch = snap.ScratchDirectory;
                using var cmd = snap.Connection.CreateCommand();
                cmd.CommandText = "SELECT x FROM t";
                Assert.Equal("hi", (string)cmd.ExecuteScalar()!);
            }
            // If a scratch copy was made, it must be gone after dispose.
            if (scratch != null) Assert.False(Directory.Exists(scratch));
        }
        finally { File.Delete(db); }
    }
}

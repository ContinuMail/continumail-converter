// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using Mail2Pst.Core.Mork;
using Xunit;

namespace Mail2Pst.Core.Tests.Mork;

public class MorkReaderSharedReadTests
{
    private const string Msf =
        "< <(a=c)> (80=ns:msg:db:row:scope:msgs:all)(96=ns:msg:db:table:kind:msgs)(88=flags) >\n" +
        "{1:^80 {(k^96:c)} [1(^88=1)] }";

    [Fact]
    public void ParseSharedReadWrite_FileHeldOpenReadWrite_Succeeds()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Msf);
            // Simulate a running Thunderbird holding the .msf open read/write with shared read/write.
            using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            MorkDocument doc = MorkReader.ParseSharedReadWrite(path);
            Assert.True(doc.TryGetSingleTable(
                "ns:msg:db:row:scope:msgs:all", "ns:msg:db:table:kind:msgs", out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_FileHeldOpenReadWrite_ThrowsIOException()
    {
        // Documents WHY the shared variant exists: plain Parse (File.OpenRead -> FileShare.Read) cannot
        // open a file another handle holds with write access.
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Msf);
            using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            Assert.Throws<IOException>(() => MorkReader.Parse(path));
        }
        finally { File.Delete(path); }
    }
}

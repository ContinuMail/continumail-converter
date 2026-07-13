// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;                   // PstOutputPlan (streaming test)
using Mail2Pst.Core.Models;                    // MailMessage / MailAttachment / AttachmentContent (streaming test)
using Mail2Pst.Core.OutlookCategories;         // StarCategory (category/flag recovery test)
using Mail2Pst.Core.Reporting;                 // ConversionReport (streaming test)
using Mail2Pst.Core.Reverse;
using Mail2Pst.Core.Writing;                   // PstWriter / PlannedMessage (streaming test)
using Mail2Pst.Integration.Tests;              // RoundTripHarness / RepoPaths live in the parent namespace
using Mail2Pst.TestSupport;
using PSTFileFormat;   // synthetic-PST construction for the non-mail-container recursion test
using Xunit;

namespace Mail2Pst.Integration.Tests.Reverse;

public class PstMailReaderTests
{
    private static (IReadOnlyList<string> outputs, string dir) ConvertProfile(ConversionConfig config)
    {
        string dir = Path.Combine(Path.GetTempPath(), "m2p-reverse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var (outputs, _) = RoundTripHarness.Convert(config, dir);
        return (outputs, dir);
    }

    [Fact]
    public void Read_EngineWrittenPst_RecoversKnownPayload()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("Inbox", messageCount: 2)
            .WithFolder("Sent", messageCount: 1)
            .Build();
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = true,
            Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
        }}};
        var (outputs, dir) = ConvertProfile(config);
        try
        {
            IReadOnlyList<PstMailFolder> folders = PstMailReader.ReadAllForTests(Assert.Single(outputs));
            Dictionary<string, int> byLeaf = folders.ToDictionary(f => f.Path[^1], f => f.Messages.Count);
            Assert.Equal(2, byLeaf["Inbox"]);
            Assert.Equal(1, byLeaf["Sent"]);

            // ThunderbirdProfileBuilder writes: Subject "Generated message N", From sender@example.com,
            // To alice@example.com, Message-ID <gen-N@example.com>, body "Synthetic body N", a Date header.
            PstMailMessage m = folders.First(f => f.Path[^1] == "Inbox").Messages[0];
            Assert.StartsWith("Generated message", m.Subject);
            Assert.Equal("sender@example.com", m.FromAddress);
            Assert.Contains(m.Recipients, r => r.Address == "alice@example.com" && r.Kind == PstRecipientKind.To);
            Assert.NotNull(m.Date);
            Assert.StartsWith("<gen-", m.MessageId);
            Assert.Contains("Synthetic body", m.PlainBody);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_PstWithContactFolder_WarnsAndSkips_ButStillReturnsMailFolders()
    {
        string mbox = RepoPaths.ResolveAgainstRepoRoot(Path.Combine("fixtures", "sample.mbox"));
        string abook = RepoPaths.ResolveAgainstRepoRoot(
            Path.Combine("tests", "Mail2Pst.Core.Tests", "Contacts", "fixtures", "sample-abook.mab"));
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = false,
            Sources = { new SourceConfig { Type = "mbox", Path = mbox } },
            Contacts = { new ContactSourceConfig { Path = abook, Format = "thunderbird-mab" } },
        }}};
        var (outputs, dir) = ConvertProfile(config);
        try
        {
            var warnings = new List<string>();
            IReadOnlyList<PstMailFolder> folders = PstMailReader.ReadAllForTests(Assert.Single(outputs), warnings.Add);
            Assert.Contains(warnings, w => w.Contains("not a mail folder"));   // contact folder skipped + warned
            Assert.Contains(folders, f => f.Messages.Count > 0);               // mail folder still returned
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_MboxWithAttachment_RecoversVisibleAttachmentPayload()
    {
        string mbox = RepoPaths.ResolveAgainstRepoRoot(Path.Combine("fixtures", "mbox-with-attachments.mbox"));
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = false,
            Sources = { new SourceConfig { Type = "mbox", Path = mbox } },
        }}};
        var (outputs, dir) = ConvertProfile(config);
        try
        {
            // Read attachment payload DURING enumeration (OpenRead is PST-bound).
            bool sawVisibleAttachmentWithBytes = false;
            foreach (PstMailItem item in PstMailReader.EnumerateMessages(Assert.Single(outputs)))
                foreach (PstAttachment a in item.Message.Attachments)
                    if (!a.IsInline)
                    {
                        using Stream s = a.OpenRead();
                        Assert.False(string.IsNullOrEmpty(a.FileName));
                        if (s.ReadByte() != -1) sawVisibleAttachmentWithBytes = true;
                    }
            Assert.True(sawVisibleAttachmentWithBytes, "expected at least one visible attachment with non-empty bytes");
        }
        finally { Directory.Delete(dir, true); }
    }

    // OpenRead reads PidTagAttachData via PropertyContext.GetBytesProperty, which dispatches through
    // GetExternalRecordData -> GetExternalPropertyBytes. The forward AttachmentWriter has TWO write paths
    // for that property: SetBytesProperty (small, heap-inline) and SetExternalProperty (large, subnode /
    // XXBlock streaming). The tests above only exercise the small path; this one forces the streaming path
    // (StreamingThresholdBytes = 1, a >XXBlock-boundary payload) and byte-compares the recovered bytes, so
    // we prove OpenRead round-trips the streamed encoding too. Mirrors StreamingAttachmentAcceptanceTests.
    [Fact]
    public void Read_StreamedAttachment_RecoversPayloadBytes()
    {
        // 9 MB > the 8,347,696 B XXBlock boundary — exercises the full XXBlock streaming machinery.
        const int size = 9_000_000;
        byte[] payload = new byte[size];
        for (int i = 0; i < size; i++) payload[i] = (byte)((i * 31 + 7) & 0xFF);

        using AttachmentContent content = AttachmentContent.FromBytes(payload);
        var msg = new MailMessage
        {
            MessageId = "<stream-readback@test>",
            Subject = "Streaming attachment read-back",
            Attachments = new List<MailAttachment>
            {
                new() { FileName = "large.bin", MimeType = "application/octet-stream", Content = content },
            },
        };

        string dir = Path.Combine(Path.GetTempPath(), "m2p-reverse-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var plan = new PstOutputPlan { Name = "Stream", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = false };
            PlannedMessage[] planned = [ new() { Message = msg, TargetFolderPath = new[] { "Inbox" } } ];
            var writer = new PstWriter { StreamingThresholdBytes = 1 };   // force the SetExternalProperty path
            List<string> outputs = writer.WritePlan(plan, planned, dir, new ConversionReport());
            string pst = Assert.Single(outputs);

            byte[]? recovered = null;
            foreach (PstMailItem item in PstMailReader.EnumerateMessages(pst))
                foreach (PstAttachment a in item.Message.Attachments)
                    if (!a.IsInline)
                    {
                        using Stream s = a.OpenRead();
                        using var ms = new MemoryStream();
                        s.CopyTo(ms);
                        recovered = ms.ToArray();
                    }

            Assert.NotNull(recovered);
            Assert.Equal(payload, recovered);   // exact byte-equality across the streamed encode/decode
        }
        finally { Directory.Delete(dir, true); }
    }

    // MailCategoryComposer.Compose appends the synthetic "Star" category (StarCategory.Name) when
    // IsFlagged is true, alongside any real Thunderbird tags — so a flagged message's recovered
    // Categories includes "Work" AND "Star". This also covers M2 (In-Reply-To / References recovery).
    [Fact]
    public void Read_MessageWithCategoriesFlagAndThreading_RecoversAll()
    {
        var msg = new MailMessage
        {
            MessageId = "<threaded@test>",
            Subject = "Threaded, flagged, categorized message",
            InReplyTo = "<parent@example.com>",
            References = "<root@example.com> <parent@example.com>",
            IsFlagged = true,
            Categories = new List<string> { "Work" },
        };

        string dir = Path.Combine(Path.GetTempPath(), "m2p-reverse-cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var plan = new PstOutputPlan { Name = "Cat", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = false };
            PlannedMessage[] planned = [ new() { Message = msg, TargetFolderPath = new[] { "Inbox" } } ];
            var writer = new PstWriter();
            List<string> outputs = writer.WritePlan(plan, planned, dir, new ConversionReport());
            string pst = Assert.Single(outputs);

            PstMailItem item = Assert.Single(PstMailReader.EnumerateMessages(pst));
            PstMailMessage m = item.Message;

            Assert.Equal("<parent@example.com>", m.InReplyTo);
            Assert.Equal("<root@example.com> <parent@example.com>", m.References);
            Assert.Contains("Work", m.Categories);
            Assert.Contains(StarCategory.Name, m.Categories);
        }
        finally { Directory.Delete(dir, true); }
    }

    // PstWriter.WriteMessage writes both SenderName/SentRepresentingName from message.From.Name when
    // present (PstWriter.cs ~722); the reader must recover it via PidTagSentRepresentingName so a later
    // plan can emit "From: Name <addr>".
    [Fact]
    public void Read_MessageWithFromDisplayName_RecoversFromNameAndAddress()
    {
        var msg = new MailMessage
        {
            MessageId = "<fromname@test>",
            Subject = "Message with sender display name",
            From = new Mail2Pst.Core.Models.MailAddress { Name = "John Doe", Email = "john@example.com" },
        };

        string dir = Path.Combine(Path.GetTempPath(), "m2p-reverse-fromname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var plan = new PstOutputPlan { Name = "FromName", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = false };
            PlannedMessage[] planned = [ new() { Message = msg, TargetFolderPath = new[] { "Inbox" } } ];
            var writer = new PstWriter();
            List<string> outputs = writer.WritePlan(plan, planned, dir, new ConversionReport());
            string pst = Assert.Single(outputs);

            PstMailItem item = Assert.Single(PstMailReader.EnumerateMessages(pst));
            PstMailMessage m = item.Message;

            Assert.Equal("John Doe", m.FromName);
            Assert.Equal("john@example.com", m.FromAddress);
        }
        finally { Directory.Delete(dir, true); }
    }

    // AttachmentWriter.Write sets PidTagAttachContentLocation whenever AttachmentSpec.ContentLocation is
    // non-empty (AttachmentWriter.cs ~55); the reader must recover it so a later plan (MimeReconstructor)
    // can restore Content-Location on the reassembled MIME part.
    [Fact]
    public void Read_MessageWithAttachmentContentLocation_RecoversContentLocation()
    {
        var msg = new MailMessage
        {
            MessageId = "<contentloc@test>",
            Subject = "Message with attachment Content-Location",
            Attachments = new List<MailAttachment>
            {
                new()
                {
                    FileName = "inline.png", MimeType = "image/png",
                    Content = AttachmentContent.FromBytes(new byte[] { 1, 2, 3 }),
                    ContentLocation = "https://example.com/inline.png",
                },
            },
        };

        string dir = Path.Combine(Path.GetTempPath(), "m2p-reverse-contentloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var plan = new PstOutputPlan { Name = "ContentLoc", MaxSizeBytes = 100L * 1024 * 1024, IncludeEmptyFolders = false };
            PlannedMessage[] planned = [ new() { Message = msg, TargetFolderPath = new[] { "Inbox" } } ];
            var writer = new PstWriter();
            List<string> outputs = writer.WritePlan(plan, planned, dir, new ConversionReport());
            string pst = Assert.Single(outputs);

            PstMailItem item = Assert.Single(PstMailReader.EnumerateMessages(pst));
            PstAttachment att = Assert.Single(item.Message.Attachments);

            Assert.Equal("https://example.com/inline.png", att.ContentLocation);
        }
        finally { Directory.Delete(dir, true); }
    }

    // EnumerateFolders is the structure authority for the export runner: it must include EMPTY mail
    // folders (which EnumerateMessages never yields), so MboxTreeWriter can honor --include-empty and
    // structural parents.
    [Fact]
    public void EnumerateFolders_IncludesEmptyFolders_ThatMessageStreamOmits()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("Inbox", messageCount: 2)
            .WithFolder("Archive", messageCount: 0)   // empty leaf
            .Build();
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = true,
            Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
        }}};
        var (outputs, dir) = ConvertProfile(config);
        try
        {
            string pst = Assert.Single(outputs);

            var folderLeaves = PstMailReader.EnumerateFolders(pst).Select(p => p[^1]).ToList();
            Assert.Contains("Inbox", folderLeaves);
            Assert.Contains("Archive", folderLeaves);   // EMPTY folder IS present in the structure authority

            var messageBearingLeaves = PstMailReader.EnumerateMessages(pst)
                .Select(i => i.FolderPath[^1]).Distinct().ToList();
            Assert.Contains("Inbox", messageBearingLeaves);
            Assert.DoesNotContain("Archive", messageBearingLeaves);   // empty folder yields no items
        }
        finally { Directory.Delete(dir, true); }
    }

    // LOAD-BEARING for the folder-gap fix: EnumerateFolders must RECURSE THROUGH a non-mail container and
    // return its mail descendants with their FULL path (and include an empty mail leaf), while NOT returning
    // the non-mail container itself. The forward writer forces intermediate folders to mail (Note) type, so
    // this tree is built directly with the vendored API (Begin/EndSavingChanges bracket per AssociatedMessageTests).
    [Fact]
    public void EnumerateFolders_RecursesThroughNonMailContainer_ReturnsMailDescendantsWithFullPath()
    {
        string pst = Path.Combine(Path.GetTempPath(), "m2p-synth-" + Guid.NewGuid().ToString("N") + ".pst");
        PSTFile.CreateEmptyStore(pst);
        var file = new PSTFile(pst, FileAccess.ReadWrite, WriterCompatibilityMode.Outlook2007RTM);
        try
        {
            file.BeginSavingChanges();
            PSTFolder top = file.TopOfPersonalFolders;
            PSTFolder container = top.CreateChildFolder("Container", FolderItemTypeName.Contact);  // NON-mail parent
            PSTFolder mail = container.CreateChildFolder("Mail", FolderItemTypeName.Note);          // mail child
            Note note = Note.CreateNewNote(file, mail.NodeID);
            note.Subject = "hello from a nested mail folder";
            note.SaveChanges();
            mail.AddMessage(note);
            mail.SaveChanges();                                                    // flush the contents-table update
            mail.CreateChildFolder("EmptyChild", FolderItemTypeName.Note);         // empty mail leaf
            file.EndSavingChanges();
        }
        finally { file.CloseFile(); }

        try
        {
            List<IReadOnlyList<string>> folders = PstMailReader.EnumerateFolders(pst).ToList();
            var joined = folders.Select(p => string.Join("/", p)).ToList();

            Assert.Contains("Container/Mail", joined);            // mail descendant, FULL path through the non-mail parent
            Assert.Contains("Container/Mail/EmptyChild", joined); // empty mail leaf included
            Assert.DoesNotContain("Container", joined);           // the non-mail container itself is NOT a mail folder

            // The message under the non-mail parent still streams with its full path.
            PstMailItem item = Assert.Single(PstMailReader.EnumerateMessages(pst));
            Assert.Equal(new[] { "Container", "Mail" }, item.FolderPath);
        }
        finally { File.Delete(pst); }
    }

    [Fact]
    public void EnumerateMessages_OnSkipped_CleanPst_RecordsNoSkips_StreamsAll()
    {
        using GeneratedProfile profile = new ThunderbirdProfileBuilder()
            .WithAccount("alice@example.com", "imap.example.com")
            .WithFolder("Inbox", messageCount: 2)
            .Build();
        var config = new ConversionConfig { Outputs = { new()
        {
            Name = "Archive", MaxSizeMB = 50_000, FolderMapping = FolderMappingMode.Mirror, IncludeEmptyFolders = false,
            Sources = profile.Folders.Select(f => new SourceConfig { Type = "mbox", Path = f.FilePath }).ToList(),
        }}};
        var (outputs, dir) = ConvertProfile(config);
        try
        {
            var skips = new List<ExportSkip>();
            int count = PstMailReader.EnumerateMessages(Assert.Single(outputs), onWarning: null, onSkipped: skips.Add).Count();
            Assert.Equal(2, count);
            Assert.Empty(skips);
        }
        finally { Directory.Delete(dir, true); }
    }
}

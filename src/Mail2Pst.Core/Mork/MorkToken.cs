// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

namespace Mail2Pst.Core.Mork;

/// <summary>Purely lexical token kinds emitted by <see cref="MorkTokenizer"/>.</summary>
public enum MorkTokenKind
{
    /// <summary>Opening &lt; of a dict section.</summary>
    DictOpen,
    /// <summary>Closing &gt; of a dict section.</summary>
    DictClose,
    /// <summary>Opening { of a table or meta group.</summary>
    BraceOpen,
    /// <summary>Closing } of a table or meta group.</summary>
    BraceClose,
    /// <summary>Opening [ of a row.</summary>
    BracketOpen,
    /// <summary>Closing ] of a row.</summary>
    BracketClose,
    /// <summary>Opening ( of a cell or dict atom.</summary>
    ParenOpen,
    /// <summary>Closing ) of a cell or dict atom.</summary>
    ParenClose,
    /// <summary>^hexid reference; Bytes = the hex id bytes (ASCII).</summary>
    AtomRef,
    /// <summary>= separator between column and value.</summary>
    Equals,
    /// <summary>: scope/namespace separator.</summary>
    Colon,
    /// <summary>Row-cut marker; emitted when - immediately follows [.</summary>
    Cut,
    /// <summary>Transaction group start @$${hexid{@; Bytes = the hex id bytes.</summary>
    GroupStart,
    /// <summary>Transaction group commit @$$}hexid}@; Bytes = the hex id bytes.</summary>
    GroupCommit,
    /// <summary>Transaction group abort @$$!hexid!@; Bytes = the hex id bytes.</summary>
    GroupAbort,
    /// <summary>
    /// A run of text (atom id, atom value, row id, table id, etc.) with all $XX and \x
    /// escapes already resolved into raw bytes. Bytes = the decoded byte sequence.
    /// </summary>
    Text,
}

/// <summary>A single lexical token from the Mork byte stream.</summary>
public readonly struct MorkToken
{
    public MorkTokenKind Kind { get; }
    public byte[] Bytes { get; }

    public MorkToken(MorkTokenKind kind, byte[] bytes)
    {
        Kind = kind;
        Bytes = bytes;
    }

    public MorkToken(MorkTokenKind kind) : this(kind, System.Array.Empty<byte>()) { }

    public override string ToString() =>
        $"{Kind}" + (Bytes.Length > 0 ? $"({System.Text.Encoding.ASCII.GetString(Bytes)})" : "");
}

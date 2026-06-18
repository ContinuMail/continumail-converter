// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mail2Pst.Core.Mork;

/// <summary>
/// Entry point for parsing Mork (.msf) files into a <see cref="MorkDocument"/>.
/// Handles single-group documents (no cross-group append-log merge — that is Task 5).
/// </summary>
public static class MorkReader
{
    // -------------------------------------------------------------------------
    // Public / internal entry points
    // -------------------------------------------------------------------------

    public static MorkDocument Parse(string path)
    {
        using var fs = File.OpenRead(path);
        return Parse(fs);
    }

    public static MorkDocument Parse(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseBytes(ms.ToArray());
    }

    internal static MorkDocument ParseString(string text) =>
        ParseBytes(Encoding.UTF8.GetBytes(text));

    // -------------------------------------------------------------------------
    // Core assembler
    // -------------------------------------------------------------------------

    private static MorkDocument ParseBytes(byte[] bytes)
    {
        var tokens = new List<MorkToken>(new MorkTokenizer(bytes).Tokenize());
        var assembler = new MorkAssembler(tokens);
        return assembler.Assemble();
    }
}

/// <summary>
/// Stateful assembler: drives a token list produced by <see cref="MorkTokenizer"/>
/// and builds a <see cref="MorkDocument"/> (atom dictionary + tables + rows).
/// Cross-group append-log merge is out of scope (Task 5).
/// </summary>
internal sealed class MorkAssembler
{
    // ---- token cursor -------------------------------------------------------
    private readonly IReadOnlyList<MorkToken> _tokens;
    private int _pos;

    // ---- atom dictionary: hex-id string -> decoded string -------------------
    // Accumulated globally across all dicts in file order; decoded at definition
    // time using the charset active for the enclosing dict.
    private readonly Dictionary<string, string> _atoms =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // ---- output -------------------------------------------------------------
    private readonly List<MorkTable> _tables = new();

    // ---- active charset for the current dict (reset per dict) ---------------
    private Encoding _charset = Encoding.UTF8;

    public MorkAssembler(IReadOnlyList<MorkToken> tokens)
    {
        _tokens = tokens;
    }

    public MorkDocument Assemble()
    {
        while (_pos < _tokens.Count)
        {
            var tok = Current();
            switch (tok.Kind)
            {
                case MorkTokenKind.DictOpen:
                    ReadDict(nestDepth: 0);
                    break;

                case MorkTokenKind.BraceOpen:
                    ReadTable();
                    break;

                case MorkTokenKind.GroupStart:
                case MorkTokenKind.GroupCommit:
                case MorkTokenKind.GroupAbort:
                    // Transaction group markers: consume and continue (Task 5 handles merge).
                    Advance();
                    break;

                default:
                    // Skip unrecognised top-level tokens.
                    Advance();
                    break;
            }
        }

        return new MorkDocument(_tables);
    }

    // -------------------------------------------------------------------------
    // Dict parsing
    // Handles nested meta-dicts (< <(a=c)> ... >) by tracking depth.
    // -------------------------------------------------------------------------

    private void ReadDict(int nestDepth)
    {
        Expect(MorkTokenKind.DictOpen); // consume '<'

        // Reset charset to default for this dict scope.
        // Outer charset is restored when we recurse (caller's _charset is on the stack).
        var savedCharset = _charset;
        _charset = Encoding.UTF8;

        // First pass to pick up (f=charset) — Mork dicts can declare charset before atoms,
        // but real files put it first too. We do a single forward pass: charset applies to
        // atoms that follow it in the same dict.
        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.DictClose)
        {
            var tok = Current();

            if (tok.Kind == MorkTokenKind.DictOpen)
            {
                // Nested meta-dict (e.g. < <(a=c)> >) — skip recursively.
                ReadDict(nestDepth: nestDepth + 1);
                continue;
            }

            if (tok.Kind == MorkTokenKind.ParenOpen)
            {
                ReadDictCell();
                continue;
            }

            // Skip any other token inside a dict (should not normally appear).
            Advance();
        }

        Expect(MorkTokenKind.DictClose); // consume '>'

        // Restore charset after we leave this dict scope.
        // (Nested dict restores on its own; for a top-level dict we just leave the
        //  charset as-is so subsequent dicts in the file inherit nothing special —
        //  each top-level dict resets to UTF-8 at entry above.)
        if (nestDepth > 0)
            _charset = savedCharset;
        // For nestDepth==0 (top-level dict): charset set by (f=…) stays active until
        // the next top-level dict resets it.  That matches the grammar: charset is
        // per-dict, atoms decoded at definition time.
    }

    /// <summary>
    /// Reads one cell inside a dict: either <c>(hexid=value)</c> (atom definition),
    /// <c>(f=charset)</c> (charset hint), or a dict-meta cell like <c>(a=c)</c> (ignored).
    /// </summary>
    private void ReadDictCell()
    {
        Expect(MorkTokenKind.ParenOpen);

        if (_pos >= _tokens.Count)
            throw new MorkFormatException("Unterminated dict cell");

        var keyTok = Current();
        if (keyTok.Kind != MorkTokenKind.Text)
        {
            // Unexpected shape — skip to closing paren.
            SkipToParenClose();
            return;
        }

        string keyStr = Encoding.ASCII.GetString(keyTok.Bytes);
        Advance(); // consume key Text

        if (_pos >= _tokens.Count || Current().Kind != MorkTokenKind.Equals)
        {
            // No '=' — not a key=value cell; skip remainder.
            SkipToParenClose();
            return;
        }
        Advance(); // consume '='

        // Value is an optional Text token (empty when the tokenizer emits no Text after '=').
        string valueBytes = "";
        if (_pos < _tokens.Count && Current().Kind == MorkTokenKind.Text)
        {
            valueBytes = MorkValueDecoder.Decode(Current().Bytes, _charset);
            Advance();
        }

        Expect(MorkTokenKind.ParenClose);

        // Charset hint?
        if (string.Equals(keyStr, "f", StringComparison.OrdinalIgnoreCase))
        {
            _charset = MorkValueDecoder.ResolveCharset(valueBytes);
            return;
        }

        // Hex-id atom definition? (only if key is all-hex characters)
        if (IsHexId(keyStr))
        {
            string atomId = keyStr.ToUpperInvariant();
            _atoms[atomId] = valueBytes;
            return;
        }

        // Otherwise it's a dict-meta cell (e.g. (a=c)) — ignore.
    }

    // -------------------------------------------------------------------------
    // Table parsing: { id:^scope {meta} rows... }
    // -------------------------------------------------------------------------

    private void ReadTable()
    {
        Expect(MorkTokenKind.BraceOpen);

        // Table id
        string tableId = ExpectText();

        // Colon separator
        Expect(MorkTokenKind.Colon);

        // Scope atom reference ^XX
        string scopeAtomId = ExpectAtomRefId();
        string scope = ResolveAtom(scopeAtomId);

        // Meta-row: { (k^XX:c) (s=N) ... }
        string? kind = null;
        if (_pos < _tokens.Count && Current().Kind == MorkTokenKind.BraceOpen)
        {
            kind = ReadMetaRow();
        }

        // Data rows
        var rows = new Dictionary<string, MorkRow>(StringComparer.Ordinal);
        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.BraceClose)
        {
            var tok = Current();
            if (tok.Kind == MorkTokenKind.BracketOpen)
            {
                var row = ReadRow();
                rows[row.Id] = row;
            }
            else
            {
                // Skip unexpected tokens inside table body.
                Advance();
            }
        }

        Expect(MorkTokenKind.BraceClose);

        _tables.Add(new MorkTable(tableId, scope, kind, rows));
    }

    /// <summary>Reads the meta-row <c>{ (k^XX:c) ... }</c> and returns the kind string.</summary>
    private string? ReadMetaRow()
    {
        Expect(MorkTokenKind.BraceOpen);

        string? kind = null;

        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.BraceClose)
        {
            if (Current().Kind == MorkTokenKind.ParenOpen)
            {
                Advance(); // consume '('

                // Expect a Text column name (literal, not AtomRef)
                if (_pos < _tokens.Count && Current().Kind == MorkTokenKind.Text)
                {
                    string colName = Encoding.ASCII.GetString(Current().Bytes);
                    Advance();

                    // (k^XX:c) — kind cell
                    if (string.Equals(colName, "k", StringComparison.OrdinalIgnoreCase)
                        && _pos < _tokens.Count && Current().Kind == MorkTokenKind.AtomRef)
                    {
                        string kindAtomId = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
                        Advance(); // consume AtomRef
                        kind = ResolveAtom(kindAtomId);
                        // Skip remaining tokens until ')'
                        SkipToParenClose();
                    }
                    else
                    {
                        // Other meta cell (e.g. (s=N)) — skip to close paren.
                        SkipToParenClose();
                    }
                }
                else
                {
                    // Unexpected shape — skip to close paren.
                    SkipToParenClose();
                }
            }
            else
            {
                Advance();
            }
        }

        Expect(MorkTokenKind.BraceClose);
        return kind;
    }

    // -------------------------------------------------------------------------
    // Row parsing: [ id (cells...) ]
    // -------------------------------------------------------------------------

    private MorkRow ReadRow()
    {
        Expect(MorkTokenKind.BracketOpen);

        // Row id
        string rowId = ExpectText();

        var cells = new Dictionary<string, string>(StringComparer.Ordinal);

        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.BracketClose)
        {
            if (Current().Kind == MorkTokenKind.ParenOpen)
            {
                ReadCell(cells);
            }
            else
            {
                Advance();
            }
        }

        Expect(MorkTokenKind.BracketClose);

        return new MorkRow(rowId, cells);
    }

    /// <summary>
    /// Reads one cell: <c>(^col=litval)</c>, <c>(^col^valAtom)</c>, or <c>(^col=)</c>.
    /// Column is always an AtomRef; value is either a literal Text or another AtomRef.
    /// </summary>
    private void ReadCell(Dictionary<string, string> cells)
    {
        Expect(MorkTokenKind.ParenOpen);

        if (_pos >= _tokens.Count)
            throw new MorkFormatException("Unterminated cell");

        // Column: must be an AtomRef ^XX
        if (Current().Kind != MorkTokenKind.AtomRef)
        {
            SkipToParenClose();
            return;
        }

        string colAtomId = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
        Advance();
        string colName = ResolveAtom(colAtomId);

        if (_pos >= _tokens.Count)
            throw new MorkFormatException($"Unterminated cell for column '{colName}'");

        string cellValue;

        if (Current().Kind == MorkTokenKind.Equals)
        {
            Advance(); // consume '='
            // Literal value or empty
            if (_pos < _tokens.Count && Current().Kind == MorkTokenKind.Text)
            {
                cellValue = MorkValueDecoder.Decode(Current().Bytes, _charset);
                Advance();
            }
            else
            {
                cellValue = ""; // (^col=) with no Text token
            }
        }
        else if (Current().Kind == MorkTokenKind.AtomRef)
        {
            // Atom-ref value: (^col^valAtom)
            string valAtomId = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
            Advance();
            cellValue = ResolveAtom(valAtomId);
        }
        else
        {
            // Unexpected shape — skip.
            SkipToParenClose();
            return;
        }

        Expect(MorkTokenKind.ParenClose);

        cells[colName] = cellValue;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private MorkToken Current() => _tokens[_pos];

    private void Advance() => _pos++;

    private void Expect(MorkTokenKind kind)
    {
        if (_pos >= _tokens.Count)
            throw new MorkFormatException($"Expected {kind} but reached end of token stream");
        if (Current().Kind != kind)
            throw new MorkFormatException(
                $"Expected {kind} but got {Current().Kind} at token index {_pos}");
        Advance();
    }

    private string ExpectText()
    {
        if (_pos >= _tokens.Count)
            throw new MorkFormatException("Expected Text token but reached end of token stream");
        if (Current().Kind != MorkTokenKind.Text)
            throw new MorkFormatException(
                $"Expected Text but got {Current().Kind} at token index {_pos}");
        string val = Encoding.ASCII.GetString(Current().Bytes);
        Advance();
        return val;
    }

    private string ExpectAtomRefId()
    {
        if (_pos >= _tokens.Count)
            throw new MorkFormatException("Expected AtomRef token but reached end of token stream");
        if (Current().Kind != MorkTokenKind.AtomRef)
            throw new MorkFormatException(
                $"Expected AtomRef but got {Current().Kind} at token index {_pos}");
        string id = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
        Advance();
        return id;
    }

    /// <summary>
    /// Resolves a hex atom id to its decoded string value.
    /// Throws <see cref="MorkFormatException"/> if the id was never defined in any dict.
    /// </summary>
    private string ResolveAtom(string hexId)
    {
        string normalised = hexId.ToUpperInvariant();
        if (!_atoms.TryGetValue(normalised, out string? value))
            throw new MorkFormatException($"Undefined atom reference: ^{hexId}");
        return value;
    }

    /// <summary>
    /// Skips tokens until the next unmatched <c>)</c>, then consumes it.
    /// Used for malformed or ignored cells.
    /// </summary>
    private void SkipToParenClose()
    {
        int depth = 1; // we already consumed the opening '('
        while (_pos < _tokens.Count && depth > 0)
        {
            switch (Current().Kind)
            {
                case MorkTokenKind.ParenOpen:  depth++; Advance(); break;
                case MorkTokenKind.ParenClose: depth--; Advance(); break;
                default: Advance(); break;
            }
        }
    }

    /// <summary>Returns true if <paramref name="s"/> is a valid hex integer string (0–9, A–F).</summary>
    private static bool IsHexId(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}

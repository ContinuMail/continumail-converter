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
/// Applies full append-log merge semantics across transaction groups.
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
/// Applies append-log merge semantics: transaction groups are processed in file
/// order; row restatements overwrite/add named cells (unnamed cells retained);
/// row cuts remove the row; delete-then-re-add recreates it. Last-write-wins per
/// (table, row, column). No cell-cut form exists in Thunderbird .msf (Task 0).
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
        new Dictionary<string, string>(StringComparer.Ordinal);

    // ---- mutable working state: table id -> (scope, kind, rows) ------------
    // Accumulated in file order to implement append-log merge. The final
    // immutable MorkDocument is built from this at the end of Assemble().
    // Table ordering is preserved via _tableOrder.
    private readonly Dictionary<string, WorkingTable> _workingTables =
        new Dictionary<string, WorkingTable>(StringComparer.Ordinal);
    private readonly List<string> _tableOrder = new();

    // ---- active charset: file-level, persists across top-level dicts -------
    // Initialised once to UTF-8. Updated by (f=<charset>) cells. Top-level dicts
    // inherit the last-set charset (no reset); nested dicts save/restore it so
    // an inner < <(a=c)> … > scope cannot permanently change the outer charset.
    private Encoding _charset = Encoding.UTF8;

    /// <summary>Mutable working state for a single table during assembly.</summary>
    private sealed class WorkingTable
    {
        public string? Scope { get; set; }
        public string? Kind { get; set; }
        // rowId -> (cells dict, or null if the row has been cut and not re-added)
        public readonly Dictionary<string, Dictionary<string, string>?> Rows =
            new Dictionary<string, Dictionary<string, string>?>(StringComparer.Ordinal);
    }

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
                    // Transaction group markers carry no merge meaning beyond ordering;
                    // their content (dicts + table fragments) flows through the normal path.
                    Advance();
                    break;

                default:
                    // Skip unrecognised top-level tokens.
                    Advance();
                    break;
            }
        }

        // Build the final immutable MorkDocument from accumulated working state.
        var tables = new List<MorkTable>(_workingTables.Count);
        foreach (string tableId in _tableOrder)
        {
            var wt = _workingTables[tableId];
            var rows = new Dictionary<string, MorkRow>(StringComparer.Ordinal);
            foreach (var kv in wt.Rows)
            {
                // Rows that were cut (null cells dict) are excluded from the output.
                if (kv.Value is not null)
                    rows[kv.Key] = new MorkRow(kv.Key, kv.Value);
            }
            tables.Add(new MorkTable(tableId, wt.Scope, wt.Kind, rows));
        }

        return new MorkDocument(tables);
    }

    // -------------------------------------------------------------------------
    // Dict parsing
    // Handles nested meta-dicts (< <(a=c)> ... >) by tracking depth.
    // -------------------------------------------------------------------------

    private void ReadDict(int nestDepth)
    {
        Expect(MorkTokenKind.DictOpen); // consume '<'

        // Nested dicts (nestDepth > 0) save and restore the charset so an inner
        // < <(a=c)> … > scope cannot permanently change the outer charset.
        // Top-level dicts (nestDepth == 0) do NOT reset: the active charset persists
        // across all top-level dicts in the file (file-level charset).
        var savedCharset = _charset;
        // (savedCharset is only used on exit when nestDepth > 0; no reset here)

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

        // Restore charset on exit from a nested dict only. Top-level dict charset
        // changes (from (f=…) cells) persist for subsequent top-level dicts so that
        // value atoms defined after the first dict still use the correct encoding.
        if (nestDepth > 0)
            _charset = savedCharset;
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
        string decodedValue = "";
        if (_pos < _tokens.Count && Current().Kind == MorkTokenKind.Text)
        {
            decodedValue = MorkValueDecoder.Decode(Current().Bytes, _charset);
            Advance();
        }

        Expect(MorkTokenKind.ParenClose);

        // Charset hint?
        if (string.Equals(keyStr, "f", StringComparison.OrdinalIgnoreCase))
        {
            _charset = MorkValueDecoder.ResolveCharset(decodedValue);
            return;
        }

        // Hex-id atom definition? (only if key is all-hex characters)
        if (IsHexId(keyStr))
        {
            string atomId = keyStr.ToUpperInvariant();
            _atoms[atomId] = decodedValue;
            return;
        }

        // Otherwise it's a dict-meta cell (e.g. (a=c)) — ignore.
    }

    // -------------------------------------------------------------------------
    // Table parsing: { id:^scope {meta} rows... }
    // Merges into the working table for this id (append-log semantics).
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

        // Get-or-create the working table for this id.
        if (!_workingTables.TryGetValue(tableId, out var wt))
        {
            wt = new WorkingTable { Scope = scope };
            _workingTables[tableId] = wt;
            _tableOrder.Add(tableId);
        }
        else
        {
            // Re-statement: update scope (last-write-wins) but keep existing rows.
            wt.Scope = scope;
        }

        // Meta-row: { (k^XX:c) (s=N) ... }
        if (_pos < _tokens.Count && Current().Kind == MorkTokenKind.BraceOpen)
        {
            string? kind = ReadMetaRow();
            if (kind is not null)
                wt.Kind = kind;
        }

        // Data rows — apply merge semantics to working table.
        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.BraceClose)
        {
            if (Current().Kind == MorkTokenKind.BracketOpen)
            {
                ReadRowIntoTable(wt);
            }
            else
            {
                // Skip unexpected tokens inside table body.
                Advance();
            }
        }

        Expect(MorkTokenKind.BraceClose);
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
    // Row parsing: [ id (cells...) ] or cut [ -id ]
    // Applies append-log merge directly into the working table.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads one row bracket from the token stream and applies its effect to
    /// <paramref name="wt"/>:
    /// <list type="bullet">
    ///   <item>Normal row <c>[id (cells…)]</c> — creates the row if absent, or merges
    ///     cells into the existing row (add/overwrite named cells; unnamed cells
    ///     retained; empty value overwrites to empty string).</item>
    ///   <item>Cut row <c>[-id]</c> — removes the row from the working state (sets
    ///     its entry to null). A later re-add recreates it.</item>
    /// </list>
    /// </summary>
    private void ReadRowIntoTable(WorkingTable wt)
    {
        Expect(MorkTokenKind.BracketOpen);

        // Detect cut: [-id]
        bool isCut = _pos < _tokens.Count && Current().Kind == MorkTokenKind.Cut;
        if (isCut)
            Advance(); // consume Cut token

        // Row id
        string rowId = ExpectText();

        if (isCut)
        {
            // Skip any remaining tokens inside the bracket (there should be none, but be safe).
            while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.BracketClose)
                Advance();
            Expect(MorkTokenKind.BracketClose);

            // Mark row as cut (null = deleted).
            wt.Rows[rowId] = null;
            return;
        }

        // Normal row: read cells and merge into working state.
        // If the row was previously cut (null), re-create it with a fresh dict.
        if (!wt.Rows.TryGetValue(rowId, out var existingCells) || existingCells is null)
        {
            existingCells = new Dictionary<string, string>(StringComparer.Ordinal);
            wt.Rows[rowId] = existingCells;
        }

        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.BracketClose)
        {
            if (Current().Kind == MorkTokenKind.ParenOpen)
            {
                ReadCell(existingCells);
            }
            else
            {
                Advance();
            }
        }

        Expect(MorkTokenKind.BracketClose);
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

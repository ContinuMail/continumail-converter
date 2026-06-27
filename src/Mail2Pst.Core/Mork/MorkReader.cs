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
/// Applies append-log merge semantics across transaction groups (commit and abort groups
/// are both treated as committed; abort-rollback is not implemented but not observed in
/// real Thunderbird .msf files).
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

    /// <summary>
    /// Parses a .msf with a live-friendly share mode (<see cref="FileShare.ReadWrite"/>), so a running
    /// Thunderbird holding the file open does not cause a sharing violation. Use this for conversion-time
    /// .msf reads; <see cref="Parse(string)"/> keeps ordinary read semantics for all other callers.
    /// </summary>
    public static MorkDocument ParseSharedReadWrite(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
/// and builds a <see cref="MorkDocument"/> (atom dictionaries + tables + rows).
/// Applies append-log merge semantics: transaction groups are processed in file
/// order; row restatements overwrite/add named cells (unnamed cells retained);
/// row cuts remove the row; delete-then-re-add recreates it. Last-write-wins per
/// (table, row, column). No cell-cut form exists in Thunderbird .msf (Task 0).
///
/// Mork has TWO separate atom spaces that reuse the same numeric ids:
/// - Column atoms: dicts that contain a &lt;(a=c)&gt; marker (as a nested sub-dict or an
///   inline cell) anywhere before the first hex-id atom definition — ids map
///   to column names / scope strings / kind strings.
/// - Value atoms: all other dicts — ids map to cell values.
/// These spaces are kept separate so a value dict cannot clobber column atoms
/// that share the same hex id.
///
/// Resolution by position:
/// - Table scope ^X, meta-row kind ^X, and a cell's column ref ^X → column map.
/// - A cell's value ref ^X (the second ^X in (^col^val)) → value map.
/// </summary>
internal sealed class MorkAssembler
{
    // ---- token cursor -------------------------------------------------------
    private readonly IReadOnlyList<MorkToken> _tokens;
    private int _pos;

    // ---- atom dictionaries: hex-id string -> decoded string -----------------
    // Column atoms: populated from dicts whose first sub-dict is <(a=c)>.
    // Value atoms:  populated from all other dicts.
    // Both accumulated globally across all dicts in file order; decoded at
    // definition time using the charset active for the enclosing dict.
    private readonly Dictionary<string, string> _columnAtoms =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _valueAtoms =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // ---- mutable working state: table id -> (scope, kind, rows) ------------
    // Accumulated in file order to implement append-log merge. The final
    // immutable MorkDocument is built from this at the end of Assemble().
    // Table ordering is preserved via _tableOrder.
    private readonly Dictionary<string, WorkingTable> _workingTables =
        new Dictionary<string, WorkingTable>(StringComparer.Ordinal);
    // Parallel list that records table-id insertion order. Dictionary<> does not
    // guarantee enumeration order, so a separate list is needed to emit tables in
    // file order (matching the append-log semantics promised by MorkDocument).
    private readonly List<string> _tableOrder = new();

    // ---- active charset: file-level, persists across top-level dicts -------
    // Initialised once to UTF-8. Updated by (f=<charset>) cells. Top-level dicts
    // inherit the last-set charset (no reset); nested dicts save/restore it so
    // an inner < <(a=c)> … > scope cannot permanently change the outer charset.
    private Encoding _charset = Encoding.UTF8;

    /// <summary>Mutable working state for a single table during assembly.</summary>
    private sealed class WorkingTable
    {
        // The table's own id (the numeric id from { <id>:^scope … }). The working-table
        // dictionary is keyed by the composite (scope, id) — see ReadTable — so Id is kept
        // here to emit the final MorkTable with its real id.
        public string Id { get; set; } = "";
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
                    ReadDict(nestDepth: 0, parentIsColumn: false);
                    break;

                case MorkTokenKind.BraceOpen:
                    ReadTable();
                    break;

                case MorkTokenKind.GroupStart:
                case MorkTokenKind.GroupCommit:
                case MorkTokenKind.GroupAbort:
                    // Transaction group markers: their enclosed content (dicts + table
                    // fragments) is processed in file order through the normal path.
                    // NOTE: GroupAbort aborted-group writes are currently treated as committed
                    // — abort-rollback is not implemented; abort markers are not observed in
                    // real Thunderbird .msf files.
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
        foreach (string tableKey in _tableOrder)
        {
            var wt = _workingTables[tableKey];
            var rows = new Dictionary<string, MorkRow>(StringComparer.Ordinal);
            foreach (var kv in wt.Rows)
            {
                // Rows that were cut (null cells dict) are excluded from the output.
                if (kv.Value is not null)
                    rows[kv.Key] = new MorkRow(kv.Key, kv.Value);
            }
            // Emit the table's real id (wt.Id), not the composite (scope, id) working key.
            tables.Add(new MorkTable(wt.Id, wt.Scope, wt.Kind, rows));
        }

        return new MorkDocument(tables);
    }

    // -------------------------------------------------------------------------
    // Dict parsing
    // Handles nested meta-dicts (< <(a=c)> ... >) by tracking depth.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads one dict block.
    /// <paramref name="nestDepth"/> tracks recursion for charset save/restore.
    /// <paramref name="parentIsColumn"/> is true when the enclosing dict has already
    /// been identified as a column dict (so nested calls inherit the space).
    /// </summary>
    private void ReadDict(int nestDepth, bool parentIsColumn)
    {
        Expect(MorkTokenKind.DictOpen); // consume '<'

        // Nested dicts (nestDepth > 0) save and restore the charset so an inner
        // < <(a=c)> … > scope cannot permanently change the outer charset.
        // Top-level dicts (nestDepth == 0) do NOT reset: the active charset persists
        // across all top-level dicts in the file (file-level charset).
        var savedCharset = _charset;

        // Determine if this is a column dict by scanning ahead within the dict:
        // a column dict has an (a=c) marker (as a nested <(a=c)> sub-dict OR as an
        // inline (a=c) cell) anywhere before its first hex-id atom definition.
        // The common real-corpus layout is marker-first, but the spec allows a charset
        // cell like (f=iso-8859-1) to precede the marker — we handle both.
        // nestDepth > 0 means we are already inside a column dict and inherit the space.
        bool isColumnDict = parentIsColumn || (nestDepth == 0 && PeekIsColumnMarker());

        while (_pos < _tokens.Count && Current().Kind != MorkTokenKind.DictClose)
        {
            var tok = Current();

            if (tok.Kind == MorkTokenKind.DictOpen)
            {
                // Nested meta-dict (e.g. < <(a=c)> >) — recurse, passing the
                // isColumnDict flag so atoms inside the nested block go to the
                // right space (though nested dicts are usually just meta markers
                // with no actual atom definitions).
                ReadDict(nestDepth: nestDepth + 1, parentIsColumn: isColumnDict);
                continue;
            }

            if (tok.Kind == MorkTokenKind.ParenOpen)
            {
                ReadDictCell(isColumnDict);
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
    /// Peeks ahead without consuming tokens to determine whether this dict is a
    /// column dict. A dict is a column dict if an <c>(a=c)</c> marker appears
    /// anywhere within it — whether as a nested <c>&lt;(a=c)&gt;</c> sub-dict OR as an
    /// inline <c>(a=c)</c> cell — regardless of what atoms or charset hints precede it.
    /// The common Thunderbird real-corpus layout has the marker first
    /// (<c>&lt; &lt;(a=c)&gt; …&gt;</c>), but the marker may legitimately follow a charset
    /// hint like <c>(f=iso-8859-1)</c> or even a hex-id atom definition, so the entire
    /// dict span is scanned rather than stopping at the first atom.
    /// Returns true if the pattern is found; does NOT advance the cursor.
    /// </summary>
    private bool PeekIsColumnMarker()
    {
        // Scan the whole dict token span (depth-tracked to stay inside) looking for either:
        //   (a) a nested DictOpen immediately followed by "(a=c)" then DictClose, OR
        //   (b) an inline paren-cell with key "a" and value "c".
        // Reaching the dict's closing > without finding either → not a column dict.
        int p = _pos;
        int depth = 1; // we are already inside the outer DictOpen

        while (p < _tokens.Count && depth > 0)
        {
            var tok = _tokens[p];

            if (tok.Kind == MorkTokenKind.DictOpen)
            {
                // Nested dict: check if its first (and only) cell is the (a=c) marker.
                // Token stream after DictOpen: ParenOpen Text("a") Equals Text("c") ParenClose DictClose …
                // IsAcCell expects to start at the Text token (after the leading ParenOpen).
                int q = p + 1; // q -> ParenOpen (if (a=c) cell)
                if (q < _tokens.Count
                    && _tokens[q].Kind == MorkTokenKind.ParenOpen
                    && IsAcCell(q + 1, out _))
                {
                    // Confirmed nested <(a=c)> — this is a column dict.
                    return true;
                }
                depth++;
                p++;
                continue;
            }

            if (tok.Kind == MorkTokenKind.DictClose)
            {
                depth--;
                p++;
                continue;
            }

            if (tok.Kind == MorkTokenKind.ParenOpen)
            {
                // Inline (a=c) marker at this dict's level → column dict.
                if (depth == 1 && IsAcCell(p + 1, out _))
                    return true;

                // Any other cell (including a hex-id atom definition): skip it and keep
                // scanning. We deliberately do NOT stop at the first hex-id — a column dict
                // may define scope/kind/column atoms BEFORE its (a=c) marker, so the whole
                // dict span must be scanned. Value dicts never contain (a=c), so they fall
                // through to the end and return false (no false positives).
                p = SkipParenPeek(p);
                continue;
            }

            p++;
        }

        return false;
    }

    /// <summary>
    /// Peeks at tokens starting at <paramref name="p"/> to check whether they form
    /// an <c>(a=c)</c> meta-cell: <c>Text("a") Equals Text("c") ParenClose</c>.
    /// Sets <paramref name="pAfter"/> to the index after the ParenClose.
    /// Does NOT require a leading ParenOpen (caller checks context).
    /// </summary>
    private bool IsAcCell(int p, out int pAfter)
    {
        pAfter = p;
        if (p >= _tokens.Count || _tokens[p].Kind != MorkTokenKind.Text) return false;
        string key = Encoding.ASCII.GetString(_tokens[p].Bytes);
        if (!string.Equals(key, "a", StringComparison.OrdinalIgnoreCase)) return false;
        p++;
        if (p >= _tokens.Count || _tokens[p].Kind != MorkTokenKind.Equals) return false;
        p++;
        if (p >= _tokens.Count || _tokens[p].Kind != MorkTokenKind.Text) return false;
        string val = Encoding.ASCII.GetString(_tokens[p].Bytes);
        if (!string.Equals(val, "c", StringComparison.OrdinalIgnoreCase)) return false;
        p++;
        if (p >= _tokens.Count || _tokens[p].Kind != MorkTokenKind.ParenClose) return false;
        pAfter = p + 1;
        return true;
    }

    /// <summary>
    /// Skips a paren-delimited group starting at <paramref name="p"/> (which must point
    /// at the <c>ParenOpen</c>) and returns the index of the token after the matching
    /// <c>ParenClose</c>. Used for lookahead scanning in <see cref="PeekIsColumnMarker"/>.
    /// </summary>
    private int SkipParenPeek(int p)
    {
        int depth = 0;
        while (p < _tokens.Count)
        {
            switch (_tokens[p].Kind)
            {
                case MorkTokenKind.ParenOpen:  depth++; p++; break;
                case MorkTokenKind.ParenClose: depth--; p++; if (depth == 0) return p; break;
                default: p++; break;
            }
        }
        return p; // unterminated — return end
    }

    /// <summary>
    /// Reads one cell inside a dict: either <c>(hexid=value)</c> (atom definition),
    /// <c>(f=charset)</c> (charset hint), or a dict-meta cell like <c>(a=c)</c> (ignored).
    /// Atoms are stored in the column map or value map based on <paramref name="isColumnDict"/>.
    /// </summary>
    private void ReadDictCell(bool isColumnDict)
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

        // Charset hint? (f=charset) — update active charset; do NOT store as an atom.
        if (string.Equals(keyStr, "f", StringComparison.OrdinalIgnoreCase))
        {
            _charset = MorkValueDecoder.ResolveCharset(decodedValue);
            return;
        }

        // Dict-meta cells like (a=c) — configure the dict space but do NOT store as atoms.
        if (string.Equals(keyStr, "a", StringComparison.OrdinalIgnoreCase))
            return;

        // Hex-id atom definition? (only if key is all-hex characters)
        if (IsHexId(keyStr))
        {
            string atomId = keyStr.ToUpperInvariant();
            // Store in the appropriate atom space.
            if (isColumnDict)
                _columnAtoms[atomId] = decodedValue;
            else
                _valueAtoms[atomId] = decodedValue;
            return;
        }

        // Otherwise it's an unrecognised dict-meta cell — ignore.
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

        // A leading '-' is the Mork transaction CUT/clear marker on the table (e.g. `{-1:^80 ...}`,
        // emitted during a Thunderbird folder reparse) — it targets table `1`, it is NOT part of the id
        // and NOT a distinct table. The tokenizer only recognises the ROW-level cut (`[-id]`, after '['),
        // so without this the '-' stays attached to the id ("-1") and ReadTable forks a phantom second
        // table with the same scope/kind — which made MsfMessageReader throw "found 2 msgs tables"
        // (KB-003). Strip it so the fragment folds into the real table via the append-log merge below.
        // Hex table ids never legitimately start with '-', so this cannot collide with a real table.
        if (tableId.Length > 1 && tableId[0] == '-')
            tableId = tableId.Substring(1);

        // Colon separator
        Expect(MorkTokenKind.Colon);

        // Scope atom reference ^XX — resolved in the COLUMN map.
        string scopeAtomId = ExpectAtomRefId();
        string scope = ResolveColumnAtom(scopeAtomId);

        // Mork table identity is (id, scope), NOT id alone: real Thunderbird .msf reuses a small
        // numeric id across DIFFERENT scopes (e.g. id "1" for both the msgs table ^80 and the
        // dbfolderinfo table ^9F). Keying by id alone collapses them — the later statement clobbers
        // the earlier table's scope/kind (last-write-wins) and merges their row bags, which loses the
        // msgs table. Key by the composite (scope, id) so distinct tables stay distinct. (A space
        // cannot appear in a scope string or hex table id, so it is a safe separator.) Every table statement in
        // real .msf carries :^scope (the parser requires it), so the key is always well-defined.
        string tableKey = scope + " " + tableId;

        // Get-or-create the working table for this (scope, id).
        if (!_workingTables.TryGetValue(tableKey, out var wt))
        {
            wt = new WorkingTable { Id = tableId, Scope = scope };
            _workingTables[tableKey] = wt;
            _tableOrder.Add(tableKey);
        }
        else
        {
            // Re-statement of the SAME (scope, id): scope is identical by construction; keep rows.
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
                // Skip unexpected tokens inside table body (e.g. bare row-id tokens
                // in thread tables that use a non-standard body format).
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

                    // (k^XX:c) — kind cell; resolve ^XX in the COLUMN map.
                    if (string.Equals(colName, "k", StringComparison.OrdinalIgnoreCase)
                        && _pos < _tokens.Count && Current().Kind == MorkTokenKind.AtomRef)
                    {
                        string kindAtomId = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
                        Advance(); // consume AtomRef
                        kind = ResolveColumnAtom(kindAtomId);
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
            // Defensive: real .msf cut rows contain only [-id] with no further cells,
            // but skip any remaining tokens to stay robust against malformed input.
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
    /// Column ref ^X is resolved in the COLUMN map; value ref ^X is resolved in the VALUE map.
    /// </summary>
    private void ReadCell(Dictionary<string, string> cells)
    {
        Expect(MorkTokenKind.ParenOpen);

        if (_pos >= _tokens.Count)
            throw new MorkFormatException("Unterminated cell");

        // Column: must be an AtomRef ^XX — resolve in COLUMN map.
        if (Current().Kind != MorkTokenKind.AtomRef)
        {
            SkipToParenClose();
            return;
        }

        string colAtomId = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
        Advance();
        string colName = ResolveColumnAtom(colAtomId);

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
            // Atom-ref value: (^col^valAtom) — resolve value in VALUE map.
            string valAtomId = Encoding.ASCII.GetString(Current().Bytes).ToUpperInvariant();
            Advance();
            cellValue = ResolveValueAtom(valAtomId);
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
    /// Resolves a hex atom id to its decoded string in the COLUMN atom map.
    /// Used for: table scope, meta-row kind, cell column refs.
    /// Throws <see cref="MorkFormatException"/> if the id was never defined.
    /// </summary>
    private string ResolveColumnAtom(string hexId)
    {
        string normalised = hexId.ToUpperInvariant();
        if (!_columnAtoms.TryGetValue(normalised, out string? value))
            throw new MorkFormatException($"Undefined column atom reference: ^{hexId}");
        return value;
    }

    /// <summary>
    /// Resolves a hex atom id to its decoded string in the VALUE atom map.
    /// Used for: cell value refs (^col^val form).
    /// Throws <see cref="MorkFormatException"/> if the id was never defined.
    /// </summary>
    private string ResolveValueAtom(string hexId)
    {
        string normalised = hexId.ToUpperInvariant();
        if (!_valueAtoms.TryGetValue(normalised, out string? value))
            throw new MorkFormatException($"Undefined value atom reference: ^{hexId}");
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

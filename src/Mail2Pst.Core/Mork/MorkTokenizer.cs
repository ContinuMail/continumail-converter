// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Mail2Pst.Core.Mork;

/// <summary>
/// Purely lexical byte scanner for the Mozilla Mork (.msf) format.
/// Emits structural tokens and escape-resolved value bytes; does NOT resolve
/// atom references, charset, or semantics — those are assembler concerns.
/// </summary>
public sealed class MorkTokenizer
{
    private readonly byte[] _input;
    private int _pos;

    // Delimiter balance stack for unterminated-construct detection.
    // We track open delimiters that require a matching close.
    private readonly Stack<byte> _delimStack = new();

    public MorkTokenizer(byte[] input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>Tokenizes the full input and yields all tokens.</summary>
    /// <exception cref="MorkFormatException">
    /// Thrown on unbalanced/unterminated constructs or malformed escapes.
    /// Note: tokens are yielded lazily; callers must enumerate to trigger validation.
    /// </exception>
    public IEnumerable<MorkToken> Tokenize()
    {
        _pos = 0;
        _delimStack.Clear();

        while (_pos < _input.Length)
        {
            // Skip whitespace
            if (IsWhitespace(_input[_pos]))
            {
                _pos++;
                continue;
            }

            // Skip // line comments
            if (_pos + 1 < _input.Length && _input[_pos] == '/' && _input[_pos + 1] == '/')
            {
                SkipLineComment();
                continue;
            }

            byte b = _input[_pos];

            // @ — could be a transaction group marker (@$${…{@ or @$$}…}@)
            if (b == '@')
            {
                MorkToken? groupTok = TryReadGroupMarker();
                if (groupTok.HasValue)
                {
                    yield return groupTok.Value;
                    continue;
                }
                // Bare @ is not a recognised form — skip it (or throw if strict needed).
                // The Mork spec uses @ only in group markers; treat lone @ as format error.
                throw new MorkFormatException($"Unexpected '@' at position {_pos}");
            }

            switch (b)
            {
                case (byte)'<':
                    _delimStack.Push(b);
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.DictOpen);
                    break;

                case (byte)'>':
                    ExpectClose((byte)'<', '>');
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.DictClose);
                    break;

                case (byte)'{':
                    _delimStack.Push(b);
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.BraceOpen);
                    break;

                case (byte)'}':
                    ExpectClose((byte)'{', '}');
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.BraceClose);
                    break;

                case (byte)'[':
                {
                    _delimStack.Push(b);
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.BracketOpen);

                    // Row-cut: a '-' immediately after '[' (possibly with the '-' directly next, no whitespace)
                    if (_pos < _input.Length && _input[_pos] == '-')
                    {
                        _pos++;
                        yield return new MorkToken(MorkTokenKind.Cut);
                    }
                    break;
                }

                case (byte)']':
                    ExpectClose((byte)'[', ']');
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.BracketClose);
                    break;

                case (byte)'(':
                    _delimStack.Push(b);
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.ParenOpen);
                    break;

                case (byte)')':
                    ExpectClose((byte)'(', ')');
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.ParenClose);
                    break;

                case (byte)'=':
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.Equals);
                    // After '=' inside a paren we are in value context: read the entire
                    // value as one text run that stops only at the unescaped ')'.
                    // This preserves spaces, colons, '=' signs, etc. as literal content.
                    if (_delimStack.Count > 0 && _delimStack.Peek() == (byte)'(')
                    {
                        byte[] value = ReadValueRun();
                        if (value.Length > 0)
                            yield return new MorkToken(MorkTokenKind.Text, value);
                        // If value is empty (e.g. "(^col=)") emit no Text token;
                        // the outer loop will handle the ')' and emit ParenClose.
                    }
                    break;

                case (byte)':':
                    _pos++;
                    yield return new MorkToken(MorkTokenKind.Colon);
                    break;

                case (byte)'^':
                {
                    // Atom reference: ^hexid  — read hex digits (and letters) until a non-hex char
                    _pos++; // skip '^'
                    byte[] id = ReadAtomId();
                    yield return new MorkToken(MorkTokenKind.AtomRef, id);
                    break;
                }

                default:
                {
                    // Plain text token: a run of non-delimiter, non-whitespace bytes
                    // (atom ids, row ids, table ids, literal values outside parens)
                    byte[] text = ReadTextRun();
                    if (text.Length > 0)
                        yield return new MorkToken(MorkTokenKind.Text, text);
                    break;
                }
            }
        }

        // After exhausting input, any open delimiters left = unterminated construct
        if (_delimStack.Count > 0)
        {
            char open = (char)_delimStack.Peek();
            throw new MorkFormatException($"Unterminated '{open}' construct at end of input");
        }
    }

    // -------------------------------------------------------------------------
    // Group marker: @$${<hexid>{@  (GroupStart)  or  @$$}<hexid>}@  (GroupCommit)
    // Grammar (from Task-0 spike):
    //   GroupStart  = @$${  <hexid>  {@
    //   GroupCommit = @$$}  <hexid>  }@
    //   GroupAbort  = @$$!  <hexid>  !@   (kept in enum but not seen in the wild)
    // -------------------------------------------------------------------------
    private MorkToken? TryReadGroupMarker()
    {
        // Need at least @$$x (4 bytes) + <hexid> + x@
        int start = _pos;
        if (_pos + 3 >= _input.Length) return null;
        if (_input[_pos] != '@' || _input[_pos + 1] != '$' || _input[_pos + 2] != '$') return null;

        byte typeChar = _input[_pos + 3]; // '{', '}', or '!'
        MorkTokenKind kind;
        byte closingChar;
        switch (typeChar)
        {
            case (byte)'{': kind = MorkTokenKind.GroupStart;  closingChar = (byte)'{'; break;
            case (byte)'}': kind = MorkTokenKind.GroupCommit; closingChar = (byte)'}'; break;
            case (byte)'!': kind = MorkTokenKind.GroupAbort;  closingChar = (byte)'!'; break;
            default: return null; // not a group marker
        }

        _pos += 4; // consume @$${  or @$$}  or @$$!

        // Read the hex id up to the matching closingChar
        int idStart = _pos;
        while (_pos < _input.Length && _input[_pos] != closingChar)
            _pos++;

        if (_pos >= _input.Length)
            throw new MorkFormatException($"Unterminated transaction group marker starting at {start}");

        byte[] hexId = _input[idStart.._pos];
        _pos++; // consume the closing char (the second { or } or !)

        // Expect trailing '@'
        if (_pos >= _input.Length || _input[_pos] != '@')
            throw new MorkFormatException($"Missing trailing '@' in transaction group marker at {start}");
        _pos++; // consume @

        return new MorkToken(kind, hexId);
    }

    // -------------------------------------------------------------------------
    // Read a ^atom-id: hex digits + upper/lowercase letters until a stop char.
    // In Mork atom ids are hex (e.g. 88, A0) but allow alpha for safety.
    // -------------------------------------------------------------------------
    private byte[] ReadAtomId()
    {
        int start = _pos;
        while (_pos < _input.Length && IsAtomIdChar(_input[_pos]))
            _pos++;
        if (_pos == start)
            throw new MorkFormatException($"Empty atom reference ('^' not followed by an id) at position {_pos}");
        return _input[start.._pos];
    }

    // -------------------------------------------------------------------------
    // Read a plain text run with $XX and \x escape resolution.
    // Stops at structural characters (including ')'), delimiters, whitespace,
    // or end-of-input.  Used for atom ids, row ids, table ids, and literal
    // text that appears BEFORE '=' inside a '(' (column names, atom text, etc.).
    // For the value that follows '=' inside a '(' use ReadValueRun() instead.
    // -------------------------------------------------------------------------
    private byte[] ReadTextRun()
    {
        var buf = new List<byte>(16);

        while (_pos < _input.Length)
        {
            byte b = _input[_pos];

            // --- Escape sequences must be handled BEFORE stop-char checks so that
            //     e.g. \) is a literal ')' rather than a close-paren stop. ---
            if (TryReadEscape(buf))
                continue;

            // --- Non-escape stop conditions ---

            // Stop at ')' (unescaped close paren)
            if (b == ')')
                break;

            // Stop at any other structural character
            if (IsStopChar(b))
                break;

            // Line comment — stop the text run and let outer loop handle it
            if (b == '/' && _pos + 1 < _input.Length && _input[_pos + 1] == '/')
                break;

            // Raw byte (incl. high bytes) — pass through directly
            buf.Add(b);
            _pos++;
        }

        return buf.ToArray();
    }

    // -------------------------------------------------------------------------
    // Read a Mork value run: the text that follows '=' inside a '('.
    // Resolves $XX hex escapes and \<x> backslash escapes exactly like
    // ReadTextRun, but the ONLY non-escape stop condition is an unescaped ')'.
    // Every other byte — spaces, ':', '=', '<', '[', etc. — is literal content.
    // Returns an empty array when the next byte is ')' (empty value).
    // -------------------------------------------------------------------------
    private byte[] ReadValueRun()
    {
        var buf = new List<byte>(32);

        while (_pos < _input.Length)
        {
            byte b = _input[_pos];

            if (TryReadEscape(buf))
                continue;

            // Unescaped ')' terminates the value — leave it for the outer loop
            if (b == ')')
                break;

            // All other bytes (including spaces, ':', '=', '<', '@', etc.) are literal value
            // content. '@' is treated as a literal here because group markers (@$${…{@) only
            // appear at the top-level token stream, never inside a paren value run.
            buf.Add(b);
            _pos++;
        }

        return buf.ToArray();
    }

    // -------------------------------------------------------------------------
    // Shared escape helper used by ReadTextRun and ReadValueRun.
    // If the byte at _pos is '$', consumes a $XX hex escape (two hex digits),
    // appends the decoded byte to buf, advances _pos by 3, and returns true.
    // If the byte at _pos is '\', consumes the backslash escape or line-
    // continuation exactly as the Mork spec requires, advances _pos past
    // whatever was consumed, and returns true (even for a bare '\' at EOI
    // or a line-continuation that contributes no byte).
    // Returns false if _pos does not point at '$' or '\', leaving _pos and buf
    // unchanged so the caller can apply its own stop conditions.
    // -------------------------------------------------------------------------
    private bool TryReadEscape(List<byte> buf)
    {
        if (_pos >= _input.Length)
            return false;

        byte b = _input[_pos];

        if (b == '$')
        {
            // $XX hex escape — valid inside any text context
            if (_pos + 2 >= _input.Length)
                throw new MorkFormatException($"Incomplete $XX hex escape at position {_pos}");
            byte hi = _input[_pos + 1];
            byte lo = _input[_pos + 2];
            if (!IsHexDigit(hi) || !IsHexDigit(lo))
                throw new MorkFormatException($"Malformed $XX hex escape at position {_pos}");
            buf.Add((byte)(HexVal(hi) << 4 | HexVal(lo)));
            _pos += 3;
            return true;
        }

        if (b == '\\')
        {
            _pos++; // consume backslash
            if (_pos >= _input.Length)
                return true; // backslash at very end — treat as continuation contributing nothing

            byte next = _input[_pos];
            // Backslash at end-of-line (CR, LF, or CRLF) = line continuation, contributes nothing
            if (next == '\r' || next == '\n')
            {
                if (next == '\r' && _pos + 1 < _input.Length && _input[_pos + 1] == '\n')
                    _pos += 2;
                else
                    _pos++;
                return true;
            }
            // Any other char after backslash: emit the literal char
            buf.Add(next);
            _pos++;
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SkipLineComment()
    {
        // Skip everything up to (but not including) the next LF/CR
        while (_pos < _input.Length && _input[_pos] != '\n' && _input[_pos] != '\r')
            _pos++;
    }

    private void ExpectClose(byte expectedOpen, char closeChar)
    {
        if (_delimStack.Count == 0 || _delimStack.Peek() != expectedOpen)
            throw new MorkFormatException(
                $"Unmatched '{closeChar}' at position {_pos}: no corresponding '{(char)expectedOpen}'");
        _delimStack.Pop();
    }

    private static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\r' || b == '\n';

    private static bool IsAtomIdChar(byte b) =>
        (b >= '0' && b <= '9') ||
        (b >= 'a' && b <= 'f') ||
        (b >= 'A' && b <= 'F') ||
        // Allow broader hex-ish range: Mork uses upper hex (0-9, A-F) but be lenient
        (b >= 'g' && b <= 'z') ||
        (b >= 'G' && b <= 'Z');

    /// <summary>Characters that terminate a plain text run outside of paren context.</summary>
    private static bool IsStopChar(byte b) =>
        b == '<' || b == '>' ||
        b == '{' || b == '}' ||
        b == '[' || b == ']' ||
        b == '(' || b == ')' ||
        b == '=' || b == ':' ||
        b == '^' || b == '@' ||
        IsWhitespace(b);

    private static bool IsHexDigit(byte b) =>
        (b >= '0' && b <= '9') ||
        (b >= 'a' && b <= 'f') ||
        (b >= 'A' && b <= 'F');

    private static int HexVal(byte b)
    {
        if (b >= '0' && b <= '9') return b - '0';
        if (b >= 'a' && b <= 'f') return b - 'a' + 10;
        if (b >= 'A' && b <= 'F') return b - 'A' + 10;
        return 0;
    }
}

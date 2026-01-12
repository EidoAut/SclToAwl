using System;

namespace SclToAwl.Lexer;

/// <summary>
/// Manual lexer for ladderizable SCL.
/// </summary>
public sealed class Lexer {
    private readonly string _text;
    private int _pos;

    /// <summary>
    /// Creates a lexer for the provided SCL source text.
    /// </summary>
    public Lexer(string text) {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Reads the next token from the input stream.
    /// </summary>
    public Token NextToken() {
        SkipTrivia();

        if (_pos >= _text.Length) {
            return new Token(TokenKind.EndOfFile, string.Empty, _pos);
        }

        var c = _text[_pos];

        // Two-character operators first
        if (c == ':' && Peek(1) == '=') return Advance(TokenKind.Assign, ":=");
        if (c == '<' && Peek(1) == '=') return Advance(TokenKind.Le, "<=");
        if (c == '>' && Peek(1) == '=') return Advance(TokenKind.Ge, ">=");
        if (c == '<' && Peek(1) == '>') return Advance(TokenKind.Ne, "<>");

        // Single-character punctuation
        if (c == ';') return Advance(TokenKind.Semicolon, ";");
        if (c == ':') return Advance(TokenKind.Colon, ":");
        if (c == ',') return Advance(TokenKind.Comma, ",");
        if (c == '(') return Advance(TokenKind.LParen, "(");
        if (c == ')') return Advance(TokenKind.RParen, ")");
        if (c == '[') return Advance(TokenKind.LBracket, "[");
        if (c == ']') return Advance(TokenKind.RBracket, "]");

        // Comparisons
        if (c == '=') return Advance(TokenKind.Eq, "=");
        if (c == '<') return Advance(TokenKind.Lt, "<");
        if (c == '>') return Advance(TokenKind.Gt, ">");

        // Arithmetic
        if (c == '+') return Advance(TokenKind.Plus, "+");
        if (c == '-') return Advance(TokenKind.Minus, "-");
        if (c == '*') return Advance(TokenKind.Star, "*");
        if (c == '/') return Advance(TokenKind.Slash, "/");

        // Identifier / keyword
        if (char.IsLetter(c) || c == '_' || c == '#') {
            return ReadIdentifier();
        }

        // Integer literal
        if (char.IsDigit(c)) {
            return ReadNumber();
        }

        throw new Exception($"Unexpected character '{c}' at {_pos}");
    }

    /// <summary>
    /// Reads an identifier and resolves keywords (case-insensitive).
    /// </summary>
    private Token ReadIdentifier() {
        var start = _pos;

        while (_pos < _text.Length && IsIdentifierChar(_text[_pos])) {
            _pos++;
        }

        var t = _text[start.._pos].ToUpperInvariant();

        return t switch {
            "EXIT" => new Token(TokenKind.Exit, t, start),
            "CONTINUE" => new Token(TokenKind.Continue, t, start),
            "AND" => new Token(TokenKind.And, t, start),
            "OR" => new Token(TokenKind.Or, t, start),
            "NOT" => new Token(TokenKind.Not, t, start),
            "TRUE" => new Token(TokenKind.True, t, start),
            "FALSE" => new Token(TokenKind.False, t, start),
            "IF" => new Token(TokenKind.If, t, start),
            "THEN" => new Token(TokenKind.Then, t, start),
            "ELSE" => new Token(TokenKind.Else, t, start),
            "END_IF" => new Token(TokenKind.EndIf, t, start),
            "CASE" => new Token(TokenKind.Case, t, start),
            "OF" => new Token(TokenKind.Of, t, start),
            "END_CASE" => new Token(TokenKind.EndCase, t, start),
            "WHILE" => new Token(TokenKind.While, t, start),
            "DO" => new Token(TokenKind.Do, t, start),
            "END_WHILE" => new Token(TokenKind.EndWhile, t, start),
            "FOR" => new Token(TokenKind.For, t, start),
            "TO" => new Token(TokenKind.To, t, start),
            "END_FOR" => new Token(TokenKind.EndFor, t, start),
            _ => new Token(TokenKind.Identifier, t, start)
        };
    }

    /// <summary>
    /// Reads a non-negative integer literal.
    /// </summary>
    private Token ReadNumber() {
        var start = _pos;

        while (_pos < _text.Length && char.IsDigit(_text[_pos])) {
            _pos++;
        }

        var text = _text[start.._pos];
        return new Token(TokenKind.Number, text, start);
    }

    /// <summary>
    /// Skips whitespace and comments.
    /// </summary>
    private void SkipTrivia() {
        while (true) {
            SkipWhite();

            if (IsLineCommentStart()) {
                SkipLineComment();
                continue;
            }

            if (IsBlockCommentStart()) {
                SkipBlockComment();
                continue;
            }

            break;
        }
    }

    /// <summary>
    /// Skips any whitespace characters.
    /// </summary>
    private void SkipWhite() {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) {
            _pos++;
        }
    }

    /// <summary>
    /// Returns true if the current position starts a line comment (//).
    /// </summary>
    private bool IsLineCommentStart() {
        return _pos < _text.Length && _text[_pos] == '/' && Peek(1) == '/';
    }

    /// <summary>
    /// Returns true if the current position starts a block comment ((* ... *)).
    /// </summary>
    private bool IsBlockCommentStart() {
        return _pos < _text.Length && _text[_pos] == '(' && Peek(1) == '*';
    }

    /// <summary>
    /// Skips a // comment through the end of the line (consumes the newline if present).
    /// </summary>
    private void SkipLineComment() {
        _pos += 2; // //

        while (_pos < _text.Length && _text[_pos] != '\n') {
            _pos++;
        }

        if (_pos < _text.Length && _text[_pos] == '\n') {
            _pos++;
        }
    }

    /// <summary>
    /// Skips a (* ... *) block comment.
    /// </summary>
    private void SkipBlockComment() {
        _pos += 2; // (*

        while (_pos < _text.Length) {
            if (_text[_pos] == '*' && Peek(1) == ')') {
                _pos += 2; // *)
                return;
            }
            _pos++;
        }

        throw new Exception("Unterminated block comment (* ... *)");
    }

    /// <summary>
    /// Peeks ahead by <paramref name="offset"/> characters, returning '\0' if out of range.
    /// </summary>
    private char Peek(int offset) {
        var i = _pos + offset;
        return i < _text.Length ? _text[i] : '\0';
    }

    /// <summary>
    /// Consumes a known token text and returns the corresponding token.
    /// </summary>
    private Token Advance(TokenKind kind, string text) {
        var start = _pos;
        _pos += text.Length;
        return new Token(kind, text, start);
    }

    /// <summary>
    /// Returns true if the character is allowed in identifiers (including '.' and '#').
    /// </summary>
    private static bool IsIdentifierChar(char ch) {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '#';
    }
}

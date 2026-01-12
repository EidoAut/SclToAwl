namespace SclToAwl.Lexer;

/// <summary>
/// All supported token kinds.
/// </summary>
public enum TokenKind
{
    Identifier,
    Number,

    // Flow-control keywords
    Exit,
    Continue,
    True,
    False,
    And,
    Or,
    Not,

    // Comparisons
    Eq,        // =
    Ne,        // <>
    Lt,        // <
    Le,        // <=
    Gt,        // >
    Ge,        // >=

    // Arithmetic
    Plus,      // +
    Minus,     // -
    Star,      // *
    Slash,     // /
    Assign,
    Semicolon,
    Comma,
    LParen,
    RParen,

    // Indexing
    LBracket,  // [
    RBracket,  // ]
    If,
    Then,
    Else,
    EndIf,
    Case,
    Of,
    EndCase,
    Colon,   // :

    While,
    Do,
    EndWhile,

    For,
    To,
    EndFor,

    EndOfFile
}

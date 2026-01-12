namespace SclToAwl.Lexer;

/// <summary>
/// Represents a lexical token.
/// </summary>
public sealed record Token(TokenKind Kind, string Text, int Position);

namespace SclToAwl.Ast;

/// <summary>
/// Integer literal expression (e.g., 0, 10, 123).
/// </summary>
public sealed record IntLiteralExpr(int Value) : Expr;
namespace SclToAwl.Ast;

/// <summary>
/// Boolean literal (TRUE / FALSE).
/// </summary>
public sealed record BoolLiteralExpr(bool Value) : Expr;

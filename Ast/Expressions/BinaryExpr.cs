namespace SclToAwl.Ast;

/// <summary>
/// Binary boolean expression (AND / OR).
/// </summary>
public sealed record BinaryExpr(string Operator, Expr Left, Expr Right) : Expr;

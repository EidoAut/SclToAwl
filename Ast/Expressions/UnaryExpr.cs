namespace SclToAwl.Ast;

/// <summary>
/// Unary expression such as NOT X.
/// </summary>
public sealed record UnaryExpr(string Operator, Expr Operand) : Expr;

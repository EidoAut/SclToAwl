namespace SclToAwl.Ast;

/// <summary>
/// Variable reference expression.
/// </summary>
public sealed record VarExpr(string Name) : Expr;

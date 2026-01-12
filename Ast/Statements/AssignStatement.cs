namespace SclToAwl.Ast;

/// <summary>
/// Assignment statement (X := expr).
/// </summary>
public sealed record AssignStatement(LValue Target, Expr Expression) : Statement;

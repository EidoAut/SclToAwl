namespace SclToAwl.Ast;

/// <summary>
/// Array access expression, e.g. ARR[3] or ARR[i].
/// Constant indices are typically lowered directly to ARR[3].
/// Non-constant indices are lowered in convert-mode via selection logic.
/// </summary>
public sealed record ArrayAccessExpr(string Name, Expr Index) : Expr;

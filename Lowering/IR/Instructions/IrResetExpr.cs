using SclToAwl.Ast;

namespace SclToAwl.Lowering;

/// <summary>
/// RESET (unlatch) a bit when a ladder-emittable boolean expression is true.
/// This avoids materializing the condition into a temporary.
/// </summary>
public sealed record IrResetExpr(string Target, Expr Condition) : IrInstruction;

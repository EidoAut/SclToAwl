using SclToAwl.Ast;

namespace SclToAwl.Lowering;

/// <summary>
/// SET (latch) a bit when a ladder-emittable boolean expression is true.
/// This avoids materializing the condition into a temporary.
/// </summary>
public sealed record IrSetExpr(string Target, Expr Condition) : IrInstruction;

using SclToAwl.Ast;

namespace SclToAwl.Lowering;

/// <summary>
/// Direct ladder-clean assignment: emits one AWL network.
/// </summary>
public sealed record IrDirectAssign(string Target, Expr Expression) : IrInstruction;
namespace SclToAwl.Lowering;

/// <summary>
/// Assignment from a computed symbol/temporary (fallback path).
/// </summary>
public sealed record IrAssign(string Target, string Source) : IrInstruction;
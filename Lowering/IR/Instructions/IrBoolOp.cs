namespace SclToAwl.Lowering;

/// <summary>
/// Boolean binary op producing a temporary.
/// </summary>
public sealed record IrBoolOp(string Target, string Op, string A, string B) : IrInstruction;
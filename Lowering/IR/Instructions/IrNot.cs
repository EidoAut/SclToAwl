namespace SclToAwl.Lowering;

/// <summary>
/// Boolean NOT producing a temporary.
/// </summary>
public sealed record IrNot(string Target, string Source) : IrInstruction;
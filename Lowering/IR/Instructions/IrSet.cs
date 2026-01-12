namespace SclToAwl.Lowering;

/// <summary>
/// SET (latch) a bit when condition is true.
/// </summary>
public sealed record IrSet(string Target, string Condition) : IrInstruction;
namespace SclToAwl.Lowering;

/// <summary>
/// RESET (unlatch) a bit when condition is true.
/// </summary>
public sealed record IrReset(string Target, string Condition) : IrInstruction;
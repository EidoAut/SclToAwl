namespace SclToAwl.Lowering;

/// <summary>
/// A textual label used by AWL jumps.
/// </summary>
public sealed record IrLabel(string Name) : IrInstruction;
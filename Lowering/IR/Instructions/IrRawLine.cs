namespace SclToAwl.Lowering;

/// <summary>
/// Emits a raw AWL line as-is.
/// </summary>
public sealed record IrRawLine(string Text) : IrInstruction;
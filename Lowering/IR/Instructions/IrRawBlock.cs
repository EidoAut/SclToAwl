using System.Collections.Generic;

namespace SclToAwl.Lowering;

/// <summary>
/// Represents a raw AWL block (lines emitted as-is).
/// Useful for control flow (labels/jumps) and constructs not yet modeled in IR.
/// </summary>
public sealed record IrRawBlock(
    List<string> Lines
) : IrInstruction;

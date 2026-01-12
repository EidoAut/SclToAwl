using System.Collections.Generic;

namespace SclToAwl.Lowering;

/// <summary>
/// Represents a CALL to a function/block with named arguments.
/// </summary>
public sealed record IrCall(
    string Name,
    List<(string ArgName, string ArgValue)> Arguments
) : IrInstruction;

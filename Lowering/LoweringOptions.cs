namespace SclToAwl.Lowering;

/// <summary>
/// Options controlling how SCL is lowered to IR.
/// </summary>
/// <param name="ladderOnly">If true, only ladder-clean networks (no temporaries/flow).</param>
/// <param name="debugView">If true, emit DBG_ networks for monitoring.</param>
/// <param name="loopIterationsPerScan">Max iterations per scan for FOR/WHILE when lowering flow constructs (convert mode).</param>
/// <param name="maxDynamicArrayIndex">Max index to unroll when lowering ARR[i] in convert mode.</param>
/// <param name="prefix">Prefix applied to generated temporaries and labels (useful when converting per-network snippets).</param>
public sealed record LoweringOptions(
    bool ladderOnly,
    bool debugView,
    int loopIterationsPerScan,
    // Default to 7 to match common small demo arrays and avoid accidental out-of-range
    // generation when the PLC array is defined as [0..7]. Users can raise this via CLI.
    int maxDynamicArrayIndex = 7,
    string prefix = ""
);

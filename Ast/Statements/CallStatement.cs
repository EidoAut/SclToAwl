using System.Collections.Generic;

namespace SclToAwl.Ast;

/// <summary>
/// Function/FB call statement like:
///   TON1(IN := A, PT := 5);
/// </summary>
/// <param name="Name">Call target name (function/block/instance name).</param>
/// <param name="Arguments">Named arguments (paramName -> expression).</param>
public sealed record CallStatement(
    string Name,
    Dictionary<string, Expr> Arguments
) : Statement;

/// <summary>
/// Named call argument (e.g., IN := A).
/// </summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Value">Parameter expression.</param>
public sealed record NamedArgument(string Name, Expr Value);
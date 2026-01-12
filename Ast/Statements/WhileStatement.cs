using System.Collections.Generic;

namespace SclToAwl.Ast;

/// <summary>
/// WHILE loop statement.
/// </summary>
public sealed record WhileStatement(
    Expr Condition,
    List<Statement> Body
) : Statement;
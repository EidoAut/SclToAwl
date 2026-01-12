using System.Collections.Generic;

namespace SclToAwl.Ast;

/// <summary>
/// FOR loop statement.
/// Example:
///   FOR i := 0 TO 10 DO
///     ...
///   END_FOR;
/// </summary>
public sealed record ForStatement(
    string Iterator,
    Expr Start,
    Expr End,
    List<Statement> Body
) : Statement;
using System.Collections.Generic;

namespace SclToAwl.Ast;

/// <summary>
/// CASE statement.
/// Example:
///   CASE Mode OF
///     0: X := TRUE;
///     1,2: Y := TRUE;
///   ELSE
///     Z := TRUE;
///   END_CASE;
/// </summary>
public sealed record CaseStatement(
    Expr Selector,
    IReadOnlyList<CaseArm> Arms,
    List<Statement>? ElseStatements
) : Statement;

/// <summary>
/// One CASE arm with one or more integer labels and its statement block.
/// </summary>
public sealed record CaseArm(
    IReadOnlyList<int> Labels,
    List<Statement> Statements
);
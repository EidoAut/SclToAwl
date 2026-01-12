using System.Collections.Generic;

namespace SclToAwl.Ast;

/// <summary>
/// IF / ELSE conditional statement.
/// </summary>
public sealed record IfStatement(
    Expr Condition,
    List<Statement> ThenStatements,
    List<Statement>? ElseStatements
) : Statement;

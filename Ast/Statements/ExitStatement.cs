namespace SclToAwl.Ast;

/// <summary>
/// EXIT statement (breaks out of the nearest loop).
/// </summary>
public sealed record ExitStatement() : Statement;

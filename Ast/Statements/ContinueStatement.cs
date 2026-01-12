namespace SclToAwl.Ast;

/// <summary>
/// CONTINUE statement (skips to next iteration of the nearest loop).
/// </summary>
public sealed record ContinueStatement() : Statement;

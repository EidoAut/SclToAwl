namespace SclToAwl.Ast;

/// <summary>
/// Assignment target. Supports optional array indexing.
/// </summary>
public sealed record LValue(string Name, Expr? Index = null)
{
    public bool IsIndexed => Index is not null;
}

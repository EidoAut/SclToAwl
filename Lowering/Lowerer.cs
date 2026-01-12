using System;
using System.Collections.Generic;
using SclToAwl.Ast;

namespace SclToAwl.Lowering;

/// <summary>
/// Lowers the AST into an IR that can be emitted as AWL.
/// </summary>
public sealed class Lowerer {
    private readonly LoweringOptions _options;

    private int _tmp;
    private int _lbl;

    /// <summary>
    /// Loop metadata used to lower EXIT/CONTINUE.
    /// </summary>
    private sealed record LoopContext(string ContinueLabel, string BreakLabel);

    private readonly Stack<LoopContext> _loopStack = new();

    /// <summary>
    /// Creates a lowerer with the given lowering options.
    /// </summary>
    public Lowerer(LoweringOptions options) {
        _options = options;
    }

    /// <summary>
    /// Lowers a list of statements into IR instructions.
    /// </summary>
    public List<IrInstruction> Lower(List<Statement> stmts) {
        var ir = new List<IrInstruction>();

        foreach (var st in stmts) {
            LowerStmt(st, ir);
        }

        return ir;
    }

    /// <summary>
    /// Lowers a single statement.
    /// </summary>
    private void LowerStmt(Statement s, List<IrInstruction> ir) {
        switch (s) {
            case AssignStatement a:
                LowerAssign(a, ir);
                return;

            case CallStatement c:
                LowerCall(c, ir);
                return;

            case CaseStatement cs:
                LowerCase(cs, ir);
                return;

            case WhileStatement ws:
                LowerWhile(ws, ir);
                return;

            case ForStatement fs:
                LowerFor(fs, ir);
                return;

            case IfStatement i:
                LowerIf(i, ir);
                return;

            case ExitStatement:
                LowerExit(ir);
                return;

            case ContinueStatement:
                LowerContinue(ir);
                return;

            default:
                throw new NotSupportedException($"Statement not supported: {s.GetType().Name}");
        }
    }

    /// <summary>
    /// Lowers an assignment statement.
    /// </summary>
    private void LowerAssign(AssignStatement a, List<IrInstruction> ir) {
        // Dynamic index assignment (ARR[i] := ...) is supported only in convert mode by unrolling.
        if (a.Target.Index is not null && a.Target.Index is not IntLiteralExpr) {
            if (_options.ladderOnly) {
                throw new NotSupportedException("Dynamic array index assignment is not supported in strict mode.");
            }

            LowerDynamicIndexedAssign(a.Target, a.Expression, ir);
            return;
        }

        var targetSym = FormatLValueSymbol(a.Target);

        // Optional: emit DBG_* rung for boolean-ish expressions.
        if (_options.debugView && IsBoolishExpr(a.Expression)) {
            var dbg = $"DBG_{SanitizeAwlSymbol(targetSym)}";
            EmitAssignToTarget(dbg, a.Expression, ir);
            EmitAssignToTarget(targetSym, new VarExpr(dbg), ir);
            return;
        }

        EmitAssignToTarget(targetSym, a.Expression, ir);
    }

    /// <summary>
    /// Emits an assignment to an AWL target, choosing the appropriate lowering strategy.
    /// </summary>
    private void EmitAssignToTarget(string target, Expr expr, List<IrInstruction> ir) {
        if (IsSimpleBoolExpr(expr)) {
            ir.Add(new IrDirectAssign(target, expr));
            return;
        }

        if (IsIntExpr(expr)) {
            if (_options.ladderOnly) {
                throw new NotSupportedException("Integer arithmetic/loads require convert mode (strict mode).");
            }

            EmitIntAssign(target, expr, ir);
            return;
        }

        if (_options.ladderOnly) {
            throw new NotSupportedException("Expression requires temporaries (strict mode).");
        }

        var src = LowerExpr(expr, ir);
        ir.Add(new IrAssign(target, src));
    }

    /// <summary>
    /// Emits an integer assignment using classic L/T sequences.
    /// </summary>
    private void EmitIntAssign(string target, Expr expr, List<IrInstruction> ir) {
        if (expr is IntLiteralExpr n) {
            ir.Add(new IrRawLine($"L  {n.Value}"));
            ir.Add(new IrRawLine($"T  {target}"));
            ir.Add(new IrRawLine(""));
            return;
        }

        // Prefer emitting simple arithmetic directly into the accumulator (ladder-like),
        // avoiding needless temporaries like "T #N..; L #N..; T target".
        if (TryEmitSimpleIntExprToTarget(target, expr, ir)) {
            ir.Add(new IrRawLine(""));
            return;
        }

        var src = LowerExpr(expr, ir);
        ir.Add(new IrRawLine($"L  {src}"));
        ir.Add(new IrRawLine($"T  {target}"));
        ir.Add(new IrRawLine(""));
    }

    /// <summary>
    /// Emits a small subset of integer expressions directly to the final target.
    /// This is used to reduce noisy temp variables in convert mode.
    /// </summary>
    private bool TryEmitSimpleIntExprToTarget(string target, Expr expr, List<IrInstruction> ir) {
        // One-level binary arithmetic with simple operands.
        if (expr is BinaryExpr b && b.Operator is "+" or "-" or "*" or "/") {
            if (TryGetSimpleNumericOperand(b.Left, out var left) && TryGetSimpleNumericOperand(b.Right, out var right)) {
                ir.Add(new IrRawLine($"L  {left}"));
                ir.Add(new IrRawLine($"L  {right}"));
                ir.Add(new IrRawLine(ArithOpToAwl(b.Operator)));
                ir.Add(new IrRawLine($"T  {target}"));
                return true;
            }
        }

        // Unary minus with a simple operand.
        if (expr is UnaryExpr u && u.Operator == "-" && TryGetSimpleNumericOperand(u.Operand, out var op)) {
            ir.Add(new IrRawLine("L  0"));
            ir.Add(new IrRawLine($"L  {op}"));
            ir.Add(new IrRawLine("-I"));
            ir.Add(new IrRawLine($"T  {target}"));
            return true;
        }

        return false;
    }

    private static bool TryGetSimpleNumericOperand(Expr e, out string sym) {
        sym = "";
        switch (e) {
            case IntLiteralExpr n:
                sym = n.Value.ToString();
                return true;
            case VarExpr v:
                sym = v.Name;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns true for expressions that ultimately evaluate to BOOL.
    /// </summary>
    private static bool IsBoolishExpr(Expr e) {
        return e switch {
            BoolLiteralExpr => true,
            VarExpr => true,
            UnaryExpr u when u.Operator == "NOT" => IsBoolishExpr(u.Operand),
            BinaryExpr b when b.Operator is "AND" or "OR" => IsBoolishExpr(b.Left) && IsBoolishExpr(b.Right),
            BinaryExpr b when b.Operator is "=" or "<>" or "<" or "<=" or ">" or ">=" => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true for expressions that are treated as integer-producing (heuristic).
    /// </summary>
    private static bool IsIntExpr(Expr e) {
        return e switch {
            IntLiteralExpr => true,
            BinaryExpr b when b.Operator is "+" or "-" or "*" or "/" => true,
            _ => false
        };
    }

    /// <summary>
    /// Formats an LValue into an AWL symbol when the index is constant (or missing).
    /// </summary>
    private static string FormatLValueSymbol(LValue lv) {
        if (lv.Index is null) {
            return lv.Name;
        }

        if (lv.Index is IntLiteralExpr i) {
            return $"{lv.Name}[{i.Value}]";
        }

        throw new NotSupportedException("Dynamic array index target cannot be formatted as a single AWL symbol.");
    }

    /// <summary>
    /// Lowers ARR[i] := rhs by unrolling to a bounded set of constant-index writes.
    /// </summary>
    private void LowerDynamicIndexedAssign(LValue target, Expr rhs, List<IrInstruction> ir) {
        var idxSym = LowerValueToSymbol(target.Index!, ir);
        var rhsSym = LowerConditionToBoolSymbol(rhs, ir);

        var endLabel = NewLabel("ARRW_END");

        for (var k = 0; k <= _options.maxDynamicArrayIndex; k++) {
            var nextLabel = NewLabel("ARRW_NEXT");

            ir.Add(new IrRawLine($"L  {idxSym}"));
            ir.Add(new IrRawLine($"L  {k}"));
            ir.Add(new IrRawLine("==I"));
            ir.Add(new IrRawLine($"JCN {nextLabel}"));

            ir.Add(new IrRawLine($"U  {rhsSym}"));
            ir.Add(new IrRawLine($"=  {target.Name}[{k}]"));
            ir.Add(new IrRawLine($"JU {endLabel}"));
            ir.Add(new IrLabel(nextLabel));
        }

        ir.Add(new IrLabel(endLabel));
        ir.Add(new IrRawLine(""));
    }

    /// <summary>
    /// Lowers a CALL statement.
    /// </summary>
    private void LowerCall(CallStatement call, List<IrInstruction> ir) {
        ir.Add(new IrRawLine($"CALL {call.Name}"));
        ir.Add(new IrRawLine("("));

        foreach (var kv in Sorted(call.Arguments)) {
            ir.Add(new IrRawLine($"  {kv.Key} := {FormatExprInline(kv.Value)}"));
        }

        ir.Add(new IrRawLine(");"));
        ir.Add(new IrRawLine(""));
    }

    /// <summary>
    /// Enumerates dictionary entries in a stable (alphabetical) order.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, Expr>> Sorted(Dictionary<string, Expr> dict) {
        var keys = new List<string>(dict.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var k in keys) {
            yield return new KeyValuePair<string, Expr>(k, dict[k]);
        }
    }

    /// <summary>
    /// Formats a small subset of expressions inline for CALL argument lists.
    /// </summary>
    private string FormatExprInline(Expr e) {
        return e switch {
            VarExpr v => v.Name,
            BoolLiteralExpr b => b.Value ? "TRUE" : "FALSE",
            IntLiteralExpr n => n.Value.ToString(),
            _ => LowerExpr(e, new List<IrInstruction>())
        };
    }

    /// <summary>
    /// Lowers an IF statement, preferring ladder-friendly patterns when possible.
    /// </summary>
    private void LowerIf(IfStatement i, List<IrInstruction> ir) {
        if (IsIfBoolAssign(i, out var target)) {
            EmitAssignToTarget(target, i.Condition, ir);
            return;
        }

        if (IsIfSetOnly(i, out var setTarget)) {
            // Prefer emitting the condition directly into RLO (ladder-like) when possible.
            if (IsSimpleBoolExpr(i.Condition)) {
                ir.Add(new IrSetExpr(setTarget, i.Condition));
                return;
            }

            var condSym = LowerConditionToBoolSymbol(i.Condition, ir);
            ir.Add(new IrSet(setTarget, condSym));
            return;
        }

        if (IsIfResetOnly(i, out var resetTarget)) {
            // Prefer emitting the condition directly into RLO (ladder-like) when possible.
            if (IsSimpleBoolExpr(i.Condition)) {
                ir.Add(new IrResetExpr(resetTarget, i.Condition));
                return;
            }

            var condSym = LowerConditionToBoolSymbol(i.Condition, ir);
            ir.Add(new IrReset(resetTarget, condSym));
            return;
        }

        if (IsIfSingleExit(i)) {
            if (_loopStack.Count == 0) {
                throw new NotSupportedException("EXIT used outside of a loop.");
            }

            LowerIfJump(i.Condition, _loopStack.Peek().BreakLabel, ir);
            return;
        }

        if (IsIfSingleContinue(i)) {
            if (_loopStack.Count == 0) {
                throw new NotSupportedException("CONTINUE used outside of a loop.");
            }

            LowerIfJump(i.Condition, _loopStack.Peek().ContinueLabel, ir);
            return;
        }

        if (!_options.ladderOnly) {
            LowerIfGeneric(i, ir);
            return;
        }

        throw new NotSupportedException("IF form not supported in current converter.");
    }

    /// <summary>
    /// Lowers a general IF/ELSE using labels and jumps (convert mode).
    /// </summary>
    private void LowerIfGeneric(IfStatement i, List<IrInstruction> ir) {
        var condSym = LowerConditionToBoolSymbol(i.Condition, ir);
        var elseLabel = NewLabel("IF_ELSE");
        var endLabel = NewLabel("IF_END");

        ir.Add(new IrRawLine($"U  {condSym}"));
        ir.Add(new IrRawLine($"JCN {elseLabel}"));

        foreach (var st in i.ThenStatements) {
            LowerStmt(st, ir);
        }

        ir.Add(new IrRawLine($"JU {endLabel}"));
        ir.Add(new IrLabel(elseLabel));

        if (i.ElseStatements is not null) {
            foreach (var st in i.ElseStatements) {
                LowerStmt(st, ir);
            }
        }

        ir.Add(new IrLabel(endLabel));
    }

    /// <summary>
    /// Lowers "IF cond THEN JU targetLabel" as a small jump block.
    /// </summary>
    private void LowerIfJump(Expr cond, string targetLabel, List<IrInstruction> ir) {
        var condSym = LowerConditionToBoolSymbol(cond, ir);
        var noJump = NewLabel("IF_NOJUMP");

        ir.Add(new IrRawLine($"U  {condSym}"));
        ir.Add(new IrRawLine($"JCN {noJump}"));
        ir.Add(new IrRawLine($"JU {targetLabel}"));
        ir.Add(new IrLabel(noJump));
    }

    /// <summary>
    /// Ensures a boolean symbol suitable for U/JCN (may materialize into a temp).
    /// </summary>
    private string LowerConditionToBoolSymbol(Expr cond, List<IrInstruction> ir) {
        var t = NewCondTemp();

        if (IsSimpleBoolExpr(cond)) {
            ir.Add(new IrDirectAssign(t, cond));
            return t;
        }

        if (_options.ladderOnly) {
            throw new NotSupportedException("Condition requires temporaries (strict mode).");
        }

        var src = LowerExpr(cond, ir);
        ir.Add(new IrAssign(t, src));
        return t;
    }

    /// <summary>
    /// Recognizes IF cond THEN X:=TRUE ELSE X:=FALSE and extracts X.
    /// </summary>
    private static bool IsIfBoolAssign(IfStatement i, out string target) {
        target = "";

        if (i.ThenStatements.Count != 1) return false;
        if (i.ElseStatements == null || i.ElseStatements.Count != 1) return false;

        if (i.ThenStatements[0] is not AssignStatement t) return false;
        if (i.ElseStatements[0] is not AssignStatement f) return false;

        if (t.Expression is not BoolLiteralExpr tb || f.Expression is not BoolLiteralExpr fb) return false;
        if (!tb.Value || fb.Value) return false;

        if (t.Target.Index is not null && t.Target.Index is not IntLiteralExpr) return false;
        if (f.Target.Index is not null && f.Target.Index is not IntLiteralExpr) return false;

        var tSym = FormatLValueSymbol(t.Target);
        var fSym = FormatLValueSymbol(f.Target);
        if (!string.Equals(tSym, fSym, StringComparison.OrdinalIgnoreCase)) return false;

        target = tSym;
        return true;
    }

    /// <summary>
    /// Recognizes IF cond THEN X:=TRUE; END_IF and extracts X.
    /// </summary>
    private static bool IsIfSetOnly(IfStatement i, out string target) {
        target = "";

        if (i.ElseStatements != null) return false;
        if (i.ThenStatements.Count != 1) return false;
        if (i.ThenStatements[0] is not AssignStatement a) return false;
        if (a.Expression is not BoolLiteralExpr b || !b.Value) return false;
        if (a.Target.Index is not null && a.Target.Index is not IntLiteralExpr) return false;

        target = FormatLValueSymbol(a.Target);
        return true;
    }

    /// <summary>
    /// Recognizes IF cond THEN X:=FALSE; END_IF and extracts X.
    /// </summary>
    private static bool IsIfResetOnly(IfStatement i, out string target) {
        target = "";

        if (i.ElseStatements != null) return false;
        if (i.ThenStatements.Count != 1) return false;
        if (i.ThenStatements[0] is not AssignStatement a) return false;
        if (a.Expression is not BoolLiteralExpr b || b.Value) return false;
        if (a.Target.Index is not null && a.Target.Index is not IntLiteralExpr) return false;

        target = FormatLValueSymbol(a.Target);
        return true;
    }

    /// <summary>
    /// True for "IF cond THEN EXIT; END_IF".
    /// </summary>
    private static bool IsIfSingleExit(IfStatement i) {
        return i.ElseStatements == null && i.ThenStatements.Count == 1 && i.ThenStatements[0] is ExitStatement;
    }

    /// <summary>
    /// True for "IF cond THEN CONTINUE; END_IF".
    /// </summary>
    private static bool IsIfSingleContinue(IfStatement i) {
        return i.ElseStatements == null && i.ThenStatements.Count == 1 && i.ThenStatements[0] is ContinueStatement;
    }

    /// <summary>
    /// Lowers EXIT by jumping to the loop break label.
    /// </summary>
    private void LowerExit(List<IrInstruction> ir) {
        if (_loopStack.Count == 0) {
            throw new NotSupportedException("EXIT used outside of a loop.");
        }

        ir.Add(new IrRawLine($"JU {_loopStack.Peek().BreakLabel}"));
    }

    /// <summary>
    /// Lowers CONTINUE by jumping to the loop continue label.
    /// </summary>
    private void LowerContinue(List<IrInstruction> ir) {
        if (_loopStack.Count == 0) {
            throw new NotSupportedException("CONTINUE used outside of a loop.");
        }

        ir.Add(new IrRawLine($"JU {_loopStack.Peek().ContinueLabel}"));
    }

    /// <summary>
    /// Lowers CASE/OF/END_CASE with optional ELSE.
    /// </summary>
    private void LowerCase(CaseStatement cs, List<IrInstruction> ir) {
        var selector = LowerValueToSymbol(cs.Selector, ir);

        var endLabel = NewLabel("CASE_END");
        var elseLabel = cs.ElseStatements != null ? NewLabel("CASE_ELSE") : endLabel;

        var armLabels = new List<string>();
        foreach (var _ in cs.Arms) {
            armLabels.Add(NewLabel("CASE_ARM"));
        }

        for (var armIndex = 0; armIndex < cs.Arms.Count; armIndex++) {
            var arm = cs.Arms[armIndex];
            var armLabel = armLabels[armIndex];

            foreach (var lab in arm.Labels) {
                var nextCmp = NewLabel("CASE_NEXT");
                ir.Add(new IrRawLine($"L  {selector}"));
                ir.Add(new IrRawLine($"L  {lab}"));
                ir.Add(new IrRawLine("==I"));
                ir.Add(new IrRawLine($"JCN {nextCmp}"));
                ir.Add(new IrRawLine($"JU {armLabel}"));
                ir.Add(new IrLabel(nextCmp));
            }
        }

        ir.Add(new IrRawLine($"JU {elseLabel}"));

        for (var armIndex = 0; armIndex < cs.Arms.Count; armIndex++) {
            ir.Add(new IrLabel(armLabels[armIndex]));
            foreach (var st in cs.Arms[armIndex].Statements) {
                LowerStmt(st, ir);
            }
            ir.Add(new IrRawLine($"JU {endLabel}"));
        }

        if (cs.ElseStatements != null) {
            ir.Add(new IrLabel(elseLabel));
            foreach (var st in cs.ElseStatements) {
                LowerStmt(st, ir);
            }
            ir.Add(new IrRawLine($"JU {endLabel}"));
        }

        ir.Add(new IrLabel(endLabel));
        ir.Add(new IrRawLine(""));
    }

    /// <summary>
    /// Lowers a WHILE loop with a per-scan iteration budget.
    /// </summary>
    private void LowerWhile(WhileStatement w, List<IrInstruction> ir) {
        var startLabel = NewLabel("WH_START");
        var endLabel = NewLabel("WH_END");

        var bud = NewNumTemp("WH_BUD");
        ir.Add(new IrRawLine($"L  {_options.loopIterationsPerScan}"));
        ir.Add(new IrRawLine($"T  {bud}"));

        _loopStack.Push(new LoopContext(ContinueLabel: startLabel, BreakLabel: endLabel));

        ir.Add(new IrLabel(startLabel));

        ir.Add(new IrRawLine($"L  {bud}"));
        ir.Add(new IrRawLine("L  0"));
        ir.Add(new IrRawLine(">I"));
        ir.Add(new IrRawLine($"JCN {endLabel}"));

        var condSym = LowerConditionToBoolSymbol(w.Condition, ir);
        ir.Add(new IrRawLine($"U  {condSym}"));
        ir.Add(new IrRawLine($"JCN {endLabel}"));

        foreach (var st in w.Body) {
            LowerStmt(st, ir);
        }

        ir.Add(new IrRawLine($"L  {bud}"));
        ir.Add(new IrRawLine("L  1"));
        ir.Add(new IrRawLine("-I"));
        ir.Add(new IrRawLine($"T  {bud}"));

        ir.Add(new IrRawLine($"JU {startLabel}"));
        ir.Add(new IrLabel(endLabel));
        ir.Add(new IrRawLine(""));

        _loopStack.Pop();
    }

    /// <summary>
    /// Lowers a FOR loop with a per-scan iteration budget.
    /// </summary>
    private void LowerFor(ForStatement f, List<IrInstruction> ir) {
        var startLabel = NewLabel("FOR_START");
        var endLabel = NewLabel("FOR_END");

        var startVal = LowerValueToSymbol(f.Start, ir);
        ir.Add(new IrRawLine($"L  {startVal}"));
        ir.Add(new IrRawLine($"T  {f.Iterator}"));

        var bud = NewNumTemp("FOR_BUD");
        ir.Add(new IrRawLine($"L  {_options.loopIterationsPerScan}"));
        ir.Add(new IrRawLine($"T  {bud}"));

        _loopStack.Push(new LoopContext(ContinueLabel: startLabel, BreakLabel: endLabel));

        ir.Add(new IrLabel(startLabel));

        ir.Add(new IrRawLine($"L  {bud}"));
        ir.Add(new IrRawLine("L  0"));
        ir.Add(new IrRawLine(">I"));
        ir.Add(new IrRawLine($"JCN {endLabel}"));

        var endVal = LowerValueToSymbol(f.End, ir);
        ir.Add(new IrRawLine($"L  {f.Iterator}"));
        ir.Add(new IrRawLine($"L  {endVal}"));
        ir.Add(new IrRawLine(">I"));
        var bodyLabel = NewLabel("FOR_BODY");
        ir.Add(new IrRawLine($"JCN {bodyLabel}"));
        ir.Add(new IrRawLine($"JU {endLabel}"));
        ir.Add(new IrLabel(bodyLabel));

        foreach (var st in f.Body) {
            LowerStmt(st, ir);
        }

        ir.Add(new IrRawLine($"L  {f.Iterator}"));
        ir.Add(new IrRawLine("L  1"));
        ir.Add(new IrRawLine("+I"));
        ir.Add(new IrRawLine($"T  {f.Iterator}"));

        ir.Add(new IrRawLine($"L  {bud}"));
        ir.Add(new IrRawLine("L  1"));
        ir.Add(new IrRawLine("-I"));
        ir.Add(new IrRawLine($"T  {bud}"));

        ir.Add(new IrRawLine($"JU {startLabel}"));
        ir.Add(new IrLabel(endLabel));
        ir.Add(new IrRawLine(""));

        _loopStack.Pop();
    }

    /// <summary>
    /// Lowers an expression to an AWL symbol or temporary.
    /// </summary>
    private string LowerExpr(Expr e, List<IrInstruction> ir) {
        switch (e) {
            case VarExpr v:
                return v.Name;

            case ArrayAccessExpr a:
                return LowerArrayAccess(a, ir);

            case BoolLiteralExpr b:
                return b.Value ? "TRUE" : "FALSE";

            case IntLiteralExpr n:
                return n.Value.ToString();

            case UnaryExpr u:
                return LowerUnary(u, ir);

            case BinaryExpr b:
                return LowerBinary(b, ir);

            default:
                throw new NotSupportedException($"LowerExpr missing support for {e.GetType().Name}");
        }
    }

    /// <summary>
    /// Lowers array access, unrolling dynamic indices in convert mode.
    /// </summary>
    private string LowerArrayAccess(ArrayAccessExpr a, List<IrInstruction> ir) {
        if (a.Index is IntLiteralExpr idxConst) {
            return $"{a.Name}[{idxConst.Value}]";
        }

        if (_options.ladderOnly) {
            throw new NotSupportedException("Dynamic array index read is not supported in strict mode.");
        }

        var idxSym = LowerValueToSymbol(a.Index, ir);
        var t = NewTempBool();

        ir.Add(new IrRawLine("UN TRUE"));
        ir.Add(new IrRawLine($"=  {t}"));

        var endLabel = NewLabel("ARRR_END");

        for (var k = 0; k <= _options.maxDynamicArrayIndex; k++) {
            var nextLabel = NewLabel("ARRR_NEXT");
            ir.Add(new IrRawLine($"L  {idxSym}"));
            ir.Add(new IrRawLine($"L  {k}"));
            ir.Add(new IrRawLine("==I"));
            ir.Add(new IrRawLine($"JCN {nextLabel}"));
            ir.Add(new IrRawLine($"U  {a.Name}[{k}]"));
            ir.Add(new IrRawLine($"=  {t}"));
            ir.Add(new IrRawLine($"JU {endLabel}"));
            ir.Add(new IrLabel(nextLabel));
        }

        ir.Add(new IrLabel(endLabel));
        ir.Add(new IrRawLine(""));
        return t;
    }

    /// <summary>
    /// Lowers unary expressions (NOT, unary minus).
    /// </summary>
    private string LowerUnary(UnaryExpr u, List<IrInstruction> ir) {
        if (u.Operator == "NOT") {
            var src = LowerExpr(u.Operand, ir);
            var t = NewTempBool();
            ir.Add(new IrRawLine($"UN {src}"));
            ir.Add(new IrRawLine($"=  {t}"));
            ir.Add(new IrRawLine(""));
            return t;
        }

        if (u.Operator == "-") {
            var src = LowerExpr(u.Operand, ir);
            var t = NewNumTemp("N");
            ir.Add(new IrRawLine($"L  0"));
            ir.Add(new IrRawLine($"L  {src}"));
            ir.Add(new IrRawLine("-I"));
            ir.Add(new IrRawLine($"T  {t}"));
            ir.Add(new IrRawLine(""));
            return t;
        }

        throw new NotSupportedException($"Unary operator not supported: {u.Operator}");
    }

    /// <summary>
    /// Lowers binary expressions (boolean ops, comparisons, arithmetic).
    /// </summary>
    private string LowerBinary(BinaryExpr b, List<IrInstruction> ir) {
        if (b.Operator == "AND" || b.Operator == "OR") {
            var left = LowerExpr(b.Left, ir);
            var right = LowerExpr(b.Right, ir);
            var t = NewTempBool();

            ir.Add(new IrRawLine($"U  {left}"));
            ir.Add(new IrRawLine($"{(b.Operator == "AND" ? "U" : "O")}  {right}"));
            ir.Add(new IrRawLine($"=  {t}"));
            ir.Add(new IrRawLine(""));
            return t;
        }

        if (b.Operator is "=" or "<>" or "<" or "<=" or ">" or ">=") {
            var left = LowerExpr(b.Left, ir);
            var right = LowerExpr(b.Right, ir);
            var t = NewTempBool();

            ir.Add(new IrRawLine($"L  {left}"));
            ir.Add(new IrRawLine($"L  {right}"));
            ir.Add(new IrRawLine(CompareOpToAwl(b.Operator)));
            ir.Add(new IrRawLine($"=  {t}"));
            ir.Add(new IrRawLine(""));
            return t;
        }

        if (b.Operator is "+" or "-" or "*" or "/") {
            var left = LowerExpr(b.Left, ir);
            var right = LowerExpr(b.Right, ir);
            var t = NewNumTemp("N");

            ir.Add(new IrRawLine($"L  {left}"));
            ir.Add(new IrRawLine($"L  {right}"));
            ir.Add(new IrRawLine(ArithOpToAwl(b.Operator)));
            ir.Add(new IrRawLine($"T  {t}"));
            ir.Add(new IrRawLine(""));
            return t;
        }

        throw new NotSupportedException($"Binary operator not supported: {b.Operator}");
    }

    /// <summary>
    /// Maps SCL comparison operators to AWL integer compare instructions.
    /// </summary>
    private static string CompareOpToAwl(string op) {
        return op switch {
            "=" => "==I",
            "<>" => "<>I",
            "<" => "<I",
            "<=" => "<=I",
            ">" => ">I",
            ">=" => ">=I",
            _ => throw new NotSupportedException($"Compare op not supported: {op}")
        };
    }

    /// <summary>
    /// Maps SCL arithmetic operators to AWL integer arithmetic instructions.
    /// </summary>
    private static string ArithOpToAwl(string op) {
        return op switch {
            "+" => "+I",
            "-" => "-I",
            "*" => "*I",
            "/" => "/I",
            _ => throw new NotSupportedException($"Arith op not supported: {op}")
        };
    }

    /// <summary>
    /// Lowers a value used in numeric contexts to a symbol (literal/var/temp).
    /// </summary>
    private string LowerValueToSymbol(Expr e, List<IrInstruction> ir) {
        if (e is IntLiteralExpr n) return n.Value.ToString();
        if (e is VarExpr v) return v.Name;

        if (_options.ladderOnly) {
            throw new NotSupportedException("Non-trivial numeric expressions not allowed in strict mode.");
        }

        var t = NewNumTemp("N");
        var src = LowerExpr(e, ir);
        ir.Add(new IrRawLine($"L  {src}"));
        ir.Add(new IrRawLine($"T  {t}"));
        return t;
    }

    /// <summary>
    /// Returns true if an expression is ladder-clean enough for direct network emission.
    /// </summary>
    private static bool IsSimpleBoolExpr(Expr e) {
        // "Simple" here means: the boolean expression can be emitted directly as a ladder-friendly
        // sequence using U/UN and O/ON with optional O( ... ) groupings.
        // We allow arbitrary nesting of AND/OR and NOT, as long as the leaves are vars or literals.
        return e switch {
            VarExpr => true,
            BoolLiteralExpr => true,
            UnaryExpr u when u.Operator == "NOT" => IsSimpleBoolExpr(u.Operand),
            BinaryExpr b when b.Operator is "AND" or "OR" => IsSimpleBoolExpr(b.Left) && IsSimpleBoolExpr(b.Right),
            _ => false
        };
    }

    /// <summary>
    /// Converts an arbitrary symbol into an AWL-safe identifier (used for debug symbols).
    /// </summary>
    private static string SanitizeAwlSymbol(string name) {
        if (string.IsNullOrWhiteSpace(name)) return "_";

        if (name.StartsWith('#')) {
            name = name.TrimStart('#');
        }

        var sb = new System.Text.StringBuilder(name.Length);
        var lastUnderscore = false;

        foreach (var ch in name) {
            var ok = (ch >= 'A' && ch <= 'Z') ||
                     (ch >= 'a' && ch <= 'z') ||
                     (ch >= '0' && ch <= '9') ||
                     ch == '_';

            if (!ok) {
                if (ch == '[' || ch == '.' || ch == ':' || ch == ']' || ch == '(' || ch == ')' || ch == ' ') {
                    if (!lastUnderscore && ch != ']') {
                        sb.Append('_');
                        lastUnderscore = true;
                    }
                    continue;
                }

                if (!lastUnderscore) {
                    sb.Append('_');
                    lastUnderscore = true;
                }
                continue;
            }

            sb.Append(ch);
            lastUnderscore = ch == '_';
        }

        var outName = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(outName) ? "_" : outName;
    }

    /// <summary>
    /// Allocates a fresh boolean temp symbol.
    /// </summary>
    private string NewTempBool() {
        return $"#{_options.prefix}t{++_tmp}";
    }

    /// <summary>
    /// Allocates a fresh condition temp symbol.
    /// </summary>
    private string NewCondTemp() {
        return $"#{_options.prefix}COND{++_tmp}";
    }

    /// <summary>
    /// Allocates a fresh numeric temp symbol.
    /// </summary>
    private string NewNumTemp(string prefix) {
        return $"#{_options.prefix}{prefix}{++_tmp}";
    }

    /// <summary>
    /// Allocates a fresh unique label.
    /// </summary>
    private string NewLabel(string prefix) {
        return $"{_options.prefix}{prefix}_{++_lbl}";
    }
}

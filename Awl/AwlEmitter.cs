using System;
using System.Collections.Generic;
using System.Text;
using SclToAwl.Ast;
using SclToAwl.Lowering;

namespace SclToAwl.Awl;

/// <summary>
/// Emits AWL text from the lowered IR.
/// </summary>
public sealed class AwlEmitter {
    /// <summary>
    /// Converts IR instructions to AWL/STL text.
    /// </summary>
    public string Emit(List<IrInstruction> ir) {
        var sb = new StringBuilder();

        var lastLineBlank = true;

        // Appends a line and tracks whether it is blank.
        void Line(string text) {
            sb.AppendLine(text);
            lastLineBlank = string.IsNullOrWhiteSpace(text);
        }

        // Ensures exactly one blank line (no duplicates).
        void BlankLine() {
            if (!lastLineBlank) {
                sb.AppendLine();
                lastLineBlank = true;
            }
        }

        var network = 0;

        // Starts a new NETWORK section with a monotonically increasing title.
        void BeginNetwork(string title) {
            BlankLine();
            network++;
            Line("NETWORK");
            Line($"TITLE = N{network}: {title}");
        }

        // Sentinel used to preserve explicit blank lines inside buffered flow networks.
        const string RawBlankSentinel = "__RAW_BLANK__";
        var rawBuf = new List<string>();

        // Builds a human-readable title for a flow-heavy network (heuristic).
        static string BuildFlowTitle(List<string> lines) {
            foreach (var l in lines) {
                if (l == RawBlankSentinel) continue;

                var t = l.Trim();
                if (t.StartsWith("CALL ", StringComparison.OrdinalIgnoreCase)) return t;
                if (t.Contains("FOR_START_")) return "FOR loop";
                if (t.Contains("WH_START_")) return "WHILE loop";
                if (t.Contains("CASE_")) return "CASE dispatch";
                if (t.Contains("ARRR_")) return "Array read (dynamic index)";
                if (t.Contains("ARRW_")) return "Array write (dynamic index)";
                if (t.Contains("IF_ELSE_") || t.Contains("IF_END_") || t.Contains("IF_NOJUMP_")) return "IF flow";
            }

            var first = "Flow";
            foreach (var l in lines) {
                if (l == RawBlankSentinel) continue;

                var t = l.Trim();
                if (t.Length == 0) continue;

                first = t;
                break;
            }

            if (first.Length > 60) first = first.Substring(0, 60) + "...";
            return $"Flow: {first}";
        }

        // Flushes buffered raw lines into a single NETWORK for readability.
        void FlushRawAsNetwork() {
            if (rawBuf.Count == 0) return;

            BeginNetwork(BuildFlowTitle(rawBuf));

            foreach (var l in rawBuf) {
                if (l == RawBlankSentinel) {
                    BlankLine();
                    continue;
                }

                Line(l);
            }

            rawBuf.Clear();
            BlankLine();
        }

        foreach (var ins in ir) {
            switch (ins) {
                case IrRawLine raw: {
                        var t = raw.Text.TrimEnd();

                        // Preserve blank lines *within* a flow block without ending the block.
                        if (string.IsNullOrWhiteSpace(t)) {
                            if (rawBuf.Count > 0 && rawBuf[^1] != RawBlankSentinel) {
                                rawBuf.Add(RawBlankSentinel);
                            }
                            break;
                        }

                        rawBuf.Add(t);
                        break;
                    }

                case IrLabel lab:
                    FlushRawAsNetwork();
                    BlankLine();
                    Line($"{lab.Name}:");
                    BlankLine();
                    break;

                case IrDirectAssign d:
                    FlushRawAsNetwork();
                    BeginNetwork($"Assign {d.Target}");
                    EmitBoolExpr(sb, d.Expression);
                    Line($"=  {d.Target}");
                    BlankLine();
                    break;

                case IrAssign a:
                    FlushRawAsNetwork();
                    BeginNetwork($"Assign {a.Target}");
                    Line($"U  {a.Source}");
                    Line($"=  {a.Target}");
                    BlankLine();
                    break;

                case IrBoolOp b:
                    FlushRawAsNetwork();
                    BeginNetwork($"Assign {b.Target}");
                    Line($"U  {b.A}");
                    Line($"{(b.Op == "AND" ? "U" : "O")}  {b.B}");
                    Line($"=  {b.Target}");
                    BlankLine();
                    break;

                case IrNot n:
                    FlushRawAsNetwork();
                    BeginNetwork($"Assign {n.Target}");
                    Line($"UN {n.Source}");
                    Line($"=  {n.Target}");
                    BlankLine();
                    break;

                case IrSet s:
                    FlushRawAsNetwork();
                    BeginNetwork($"Set {s.Target}");
                    Line($"U  {s.Condition}");
                    Line($"S  {s.Target}");
                    BlankLine();
                    break;

                case IrSetExpr s:
                    FlushRawAsNetwork();
                    BeginNetwork($"Set {s.Target}");
                    EmitBoolExpr(sb, s.Condition);
                    Line($"S  {s.Target}");
                    BlankLine();
                    break;

                case IrReset r:
                    FlushRawAsNetwork();
                    BeginNetwork($"Reset {r.Target}");
                    Line($"U  {r.Condition}");
                    Line($"R  {r.Target}");
                    BlankLine();
                    break;

                case IrResetExpr r:
                    FlushRawAsNetwork();
                    BeginNetwork($"Reset {r.Target}");
                    EmitBoolExpr(sb, r.Condition);
                    Line($"R  {r.Target}");
                    BlankLine();
                    break;

                default:
                    throw new NotSupportedException($"Unsupported IR instruction {ins.GetType().Name}");
            }
        }

        FlushRawAsNetwork();
        return sb.ToString();
    }

    /// <summary>
    /// Converts IR instructions to AWL/STL text suitable for pasting inside a single NETWORK.
    /// Unlike <see cref="Emit"/>, this emits no NETWORK/TITLE headers and does not regroup raw flow.
    /// </summary>
    public string EmitInline(List<IrInstruction> ir) {
        var sb = new StringBuilder();

        var lastLineBlank = true;

        void Line(string text) {
            sb.AppendLine(text);
            lastLineBlank = string.IsNullOrWhiteSpace(text);
        }

        void BlankLine() {
            if (!lastLineBlank) {
                sb.AppendLine();
                lastLineBlank = true;
            }
        }

        foreach (var ins in ir) {
            switch (ins) {
                case IrRawLine raw: {
                        // Preserve raw spacing as much as possible.
                        var t = raw.Text.TrimEnd();
                        Line(t);
                        break;
                    }

                case IrLabel lab:
                    BlankLine();
                    Line($"{lab.Name}:");
                    BlankLine();
                    break;

                case IrDirectAssign d:
                    EmitBoolExpr(sb, d.Expression);
                    Line($"=  {d.Target}");
                    BlankLine();
                    break;

                case IrAssign a:
                    Line($"U  {a.Source}");
                    Line($"=  {a.Target}");
                    BlankLine();
                    break;

                case IrBoolOp b:
                    Line($"U  {b.A}");
                    Line($"{(b.Op == "AND" ? "U" : "O")}  {b.B}");
                    Line($"=  {b.Target}");
                    BlankLine();
                    break;

                case IrNot n:
                    Line($"UN {n.Source}");
                    Line($"=  {n.Target}");
                    BlankLine();
                    break;

                case IrSet s:
                    Line($"U  {s.Condition}");
                    Line($"S  {s.Target}");
                    BlankLine();
                    break;

                case IrSetExpr s:
                    EmitBoolExpr(sb, s.Condition);
                    Line($"S  {s.Target}");
                    BlankLine();
                    break;

                case IrReset r:
                    Line($"U  {r.Condition}");
                    Line($"R  {r.Target}");
                    BlankLine();
                    break;

                case IrResetExpr r:
                    EmitBoolExpr(sb, r.Condition);
                    Line($"R  {r.Target}");
                    BlankLine();
                    break;

                default:
                    throw new NotSupportedException($"Unsupported IR instruction {ins.GetType().Name}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits a ladder-friendly boolean expression (used by <see cref="IrDirectAssign"/>).
    /// </summary>
    private void EmitBoolExpr(StringBuilder sb, Expr e) {
        EmitOr(sb, e);

        // Emits OR-level expressions (left-associative).
        void EmitOr(StringBuilder s, Expr x) {
            if (x is BinaryExpr { Operator: "OR" } b) {
                EmitOr(s, b.Left);
                EmitOrBranch(s, b.Right);
                return;
            }

            EmitAnd(s, x);
        }

        // Emits the RHS of an OR in a form compatible with "O"/"ON" or "O(" ... ")".
        void EmitOrBranch(StringBuilder s, Expr branch) {
            if (TryGetSingleOperand(branch, out var op, out var negated)) {
                s.AppendLine($"{(negated ? "ON" : "O ")} {op}");
                return;
            }

            s.AppendLine("O(");
            EmitOr(s, branch);
            s.AppendLine(")");
        }

        // Emits AND-level expressions (left-associative).
        void EmitAnd(StringBuilder s, Expr x) {
            if (x is BinaryExpr { Operator: "AND" } b) {
                EmitAnd(s, b.Left);
                EmitFactor(s, b.Right);
                return;
            }

            EmitFactor(s, x);
        }

        // Emits a factor, simplifying double negations where possible.
        void EmitFactor(StringBuilder s, Expr x) {
            // Support OR sub-expressions inside an AND chain by emitting an AND bracket.
            // This avoids forcing temporaries for patterns like: A AND (B OR C)
            if (x is BinaryExpr { Operator: "OR" } orInAnd) {
                s.AppendLine("U(");
                EmitOr(s, orInAnd);
                s.AppendLine(")");
                return;
            }

            if (x is UnaryExpr { Operator: "NOT" } n &&
                n.Operand is UnaryExpr { Operator: "NOT" } nn) {
                EmitFactor(s, nn.Operand);
                return;
            }

            if (x is UnaryExpr { Operator: "NOT" } neg) {
                EmitAtom(s, neg.Operand, negated: true);
                return;
            }

            EmitAtom(s, x, negated: false);
        }

        // Emits an atomic operand (Var or Bool literal), optionally negated.
        void EmitAtom(StringBuilder s, Expr x, bool negated) {
            switch (x) {
                case VarExpr v:
                    s.AppendLine($"{(negated ? "UN" : "U ")} {v.Name}");
                    break;

                case BoolLiteralExpr b:
                    if (!negated) {
                        s.AppendLine(b.Value ? "U  TRUE" : "UN TRUE");
                    } else {
                        s.AppendLine(b.Value ? "UN TRUE" : "U  TRUE");
                    }
                    break;

                default:
                    throw new NotSupportedException("Non-atom in ladder emitter");
            }
        }

        // If possible, returns a single operand suitable for "O <op>" / "ON <op>".
        static bool TryGetSingleOperand(Expr x, out string operand, out bool negated) {
            if (x is UnaryExpr { Operator: "NOT" } u &&
                (u.Operand is VarExpr || u.Operand is BoolLiteralExpr)) {
                operand = u.Operand is VarExpr vv ? vv.Name : "TRUE";
                negated = true;

                if (u.Operand is BoolLiteralExpr bb) {
                    // NOT TRUE -> ON TRUE, NOT FALSE -> O TRUE
                    negated = bb.Value;
                }

                return true;
            }

            if (x is VarExpr v) {
                operand = v.Name;
                negated = false;
                return true;
            }

            if (x is BoolLiteralExpr b) {
                operand = "TRUE";
                negated = !b.Value; // FALSE becomes ON TRUE
                return true;
            }

            operand = "";
            negated = false;
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using SclToAwl.Ast;
using SclToAwl.Lexer;

namespace SclToAwl.Parser;

/// <summary>
/// Recursive-descent parser for ladderizable SCL.
/// </summary>
public sealed class Parser {
    private readonly Lexer.Lexer _lexer;
    private Token _current;
    private Token _next; // 1-token lookahead

    /// <summary>
    /// Creates a parser for the provided SCL source text.
    /// </summary>
    public Parser(string text) {
        _lexer = new Lexer.Lexer(text);
        _current = _lexer.NextToken();
        _next = _lexer.NextToken();
    }

    /// <summary>
    /// Parses an entire program as a list of statements.
    /// </summary>
    public List<Statement> ParseProgram() {
        var list = new List<Statement>();

        while (_current.Kind != TokenKind.EndOfFile) {
            list.Add(ParseStatement());
        }

        return list;
    }

    /// <summary>
    /// Parses a single statement based on the current token.
    /// </summary>
    private Statement ParseStatement() {
        if (_current.Kind == TokenKind.If) {
            return ParseIf();
        }

        if (_current.Kind == TokenKind.Case) {
            return ParseCase();
        }

        if (_current.Kind == TokenKind.While) {
            return ParseWhile();
        }

        if (_current.Kind == TokenKind.For) {
            return ParseFor();
        }

        if (_current.Kind == TokenKind.Exit) {
            Advance();
            Expect(TokenKind.Semicolon);
            return new ExitStatement();
        }

        if (_current.Kind == TokenKind.Continue) {
            Advance();
            Expect(TokenKind.Semicolon);
            return new ContinueStatement();
        }

        if (_current.Kind == TokenKind.Identifier) {
            // X := ...
            // ARR[0] := ...
            if (_next.Kind == TokenKind.Assign || _next.Kind == TokenKind.LBracket) {
                return ParseAssign();
            }

            // Foo(...)
            if (_next.Kind == TokenKind.LParen) {
                return ParseCall();
            }

            throw new Exception($"Unexpected token after identifier '{_current.Text}': {_next.Kind}");
        }

        throw new Exception($"Unexpected statement start: {_current.Kind}");
    }

    /// <summary>
    /// Parses an assignment statement.
    /// </summary>
    private AssignStatement ParseAssign() {
        var target = ParseAssignmentTarget();
        Expect(TokenKind.Assign);
        var expr = ParseExpr();
        Expect(TokenKind.Semicolon);
        return new AssignStatement(target, expr);
    }

    /// <summary>
    /// Parses the left-hand side of an assignment (identifier or indexed identifier).
    /// </summary>
    private LValue ParseAssignmentTarget() {
        var name = Expect(TokenKind.Identifier).Text;

        if (_current.Kind == TokenKind.LBracket) {
            Expect(TokenKind.LBracket);
            var idxExpr = ParseExpr();
            Expect(TokenKind.RBracket);
            return new LValue(name, idxExpr);
        }

        return new LValue(name);
    }

    /// <summary>
    /// Parses a call statement (identifier + named arguments).
    /// </summary>
    private CallStatement ParseCall() {
        var name = Expect(TokenKind.Identifier).Text;
        Expect(TokenKind.LParen);

        var args = new Dictionary<string, Expr>(StringComparer.OrdinalIgnoreCase);

        // Allow empty call: Foo();
        if (_current.Kind != TokenKind.RParen) {
            while (true) {
                var param = Expect(TokenKind.Identifier).Text;
                Expect(TokenKind.Assign);
                var value = ParseExpr();
                args[param] = value;

                if (_current.Kind == TokenKind.RParen) {
                    break;
                }

                Expect(TokenKind.Comma);
            }
        }

        Expect(TokenKind.RParen);
        Expect(TokenKind.Semicolon);

        return new CallStatement(name, args);
    }

    /// <summary>
    /// Parses an IF / ELSE / END_IF block.
    /// </summary>
    private IfStatement ParseIf() {
        Expect(TokenKind.If);
        var cond = ParseExpr();
        Expect(TokenKind.Then);

        var thenStmts = new List<Statement>();
        while (_current.Kind != TokenKind.Else && _current.Kind != TokenKind.EndIf) {
            thenStmts.Add(ParseStatement());
        }

        List<Statement>? elseStmts = null;
        if (_current.Kind == TokenKind.Else) {
            Advance(); // ELSE
            elseStmts = new List<Statement>();
            while (_current.Kind != TokenKind.EndIf) {
                elseStmts.Add(ParseStatement());
            }
        }

        Expect(TokenKind.EndIf);
        Expect(TokenKind.Semicolon);

        return new IfStatement(cond, thenStmts, elseStmts);
    }

    /// <summary>
    /// Parses a WHILE / DO / END_WHILE loop.
    /// </summary>
    private WhileStatement ParseWhile() {
        Expect(TokenKind.While);

        var condition = ParseExpr();
        Expect(TokenKind.Do);

        var body = new List<Statement>();
        while (_current.Kind != TokenKind.EndWhile) {
            body.Add(ParseStatement());
        }

        Expect(TokenKind.EndWhile);
        Expect(TokenKind.Semicolon);

        return new WhileStatement(condition, body);
    }

    /// <summary>
    /// Parses a FOR loop (FOR i := start TO end DO ... END_FOR).
    /// </summary>
    private ForStatement ParseFor() {
        Expect(TokenKind.For);

        var iterator = Expect(TokenKind.Identifier).Text;
        Expect(TokenKind.Assign);

        var startExpr = ParseExpr();
        Expect(TokenKind.To);

        var endExpr = ParseExpr();
        Expect(TokenKind.Do);

        var body = new List<Statement>();
        while (_current.Kind != TokenKind.EndFor) {
            body.Add(ParseStatement());
        }

        Expect(TokenKind.EndFor);
        Expect(TokenKind.Semicolon);

        return new ForStatement(iterator, startExpr, endExpr, body);
    }

    /// <summary>
    /// Parses an expression (top-level).
    /// </summary>
    private Expr ParseExpr() {
        return ParseOr();
    }

    /// <summary>
    /// Parses OR-expressions.
    /// </summary>
    private Expr ParseOr() {
        var left = ParseAnd();

        while (_current.Kind == TokenKind.Or) {
            var op = _current.Text;
            Advance();
            var right = ParseAnd();
            left = new BinaryExpr(op, left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses AND-expressions.
    /// </summary>
    private Expr ParseAnd() {
        var left = ParseCompare();

        while (_current.Kind == TokenKind.And) {
            var op = _current.Text;
            Advance();
            var right = ParseCompare();
            left = new BinaryExpr(op, left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses comparison expressions (=, &lt;&gt;, &lt;, &lt;=, &gt;, &gt;=).
    /// </summary>
    private Expr ParseCompare() {
        var left = ParseAdd();

        while (_current.Kind is TokenKind.Eq or TokenKind.Ne or TokenKind.Lt or TokenKind.Le or TokenKind.Gt or TokenKind.Ge) {
            var op = _current.Text;
            Advance();
            var right = ParseAdd();
            left = new BinaryExpr(op, left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses addition/subtraction expressions.
    /// </summary>
    private Expr ParseAdd() {
        var left = ParseMul();

        while (_current.Kind is TokenKind.Plus or TokenKind.Minus) {
            var op = _current.Text;
            Advance();
            var right = ParseMul();
            left = new BinaryExpr(op, left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses multiplication/division expressions.
    /// </summary>
    private Expr ParseMul() {
        var left = ParseUnary();

        while (_current.Kind is TokenKind.Star or TokenKind.Slash) {
            var op = _current.Text;
            Advance();
            var right = ParseUnary();
            left = new BinaryExpr(op, left, right);
        }

        return left;
    }

    /// <summary>
    /// Parses unary operators (NOT, unary minus).
    /// </summary>
    private Expr ParseUnary() {
        if (_current.Kind == TokenKind.Not) {
            Advance();
            return new UnaryExpr("NOT", ParseUnary());
        }

        if (_current.Kind == TokenKind.Minus) {
            Advance();
            return new UnaryExpr("-", ParseUnary());
        }

        return ParsePrimary();
    }

    /// <summary>
    /// Parses literals, variables, indexing, and parenthesized expressions.
    /// </summary>
    private Expr ParsePrimary() {
        if (_current.Kind is TokenKind.True or TokenKind.False) {
            var v = _current.Kind == TokenKind.True;
            Advance();
            return new BoolLiteralExpr(v);
        }

        if (_current.Kind == TokenKind.Number) {
            var text = Expect(TokenKind.Number).Text;
            if (!int.TryParse(text, out var val)) {
                throw new Exception($"Invalid integer literal: {text}");
            }
            return new IntLiteralExpr(val);
        }

        if (_current.Kind == TokenKind.Identifier) {
            var n = _current.Text;
            Advance();

            if (_current.Kind == TokenKind.LBracket) {
                Advance();

                // Preserve existing output shape for constant indices.
                if (_current.Kind == TokenKind.Number) {
                    var idxTok = Expect(TokenKind.Number);
                    Expect(TokenKind.RBracket);
                    return new VarExpr($"{n}[{idxTok.Text}]");
                }

                var idxExpr = ParseExpr();
                Expect(TokenKind.RBracket);
                return new ArrayAccessExpr(n, idxExpr);
            }

            return new VarExpr(n);
        }

        if (_current.Kind == TokenKind.LParen) {
            Advance();
            var e = ParseExpr();
            Expect(TokenKind.RParen);
            return e;
        }

        throw new Exception($"Unexpected token in expression: {_current.Kind}");
    }

    /// <summary>
    /// Advances the token stream by one token.
    /// </summary>
    private void Advance() {
        _current = _next;
        _next = _lexer.NextToken();
    }

    /// <summary>
    /// Consumes the current token if it matches <paramref name="k"/>, otherwise throws.
    /// </summary>
    private Token Expect(TokenKind k) {
        if (_current.Kind != k) {
            throw new Exception($"Expected {k}, got {_current.Kind}");
        }

        var t = _current;
        Advance();
        return t;
    }

    /// <summary>
    /// Parses a CASE / OF / END_CASE statement.
    /// </summary>
    private CaseStatement ParseCase() {
        Expect(TokenKind.Case);

        var selector = ParseExpr();
        Expect(TokenKind.Of);

        var arms = new List<CaseArm>();
        List<Statement>? elseStmts = null;

        while (_current.Kind != TokenKind.Else && _current.Kind != TokenKind.EndCase) {
            var labels = new List<int> { ParseCaseLabel() };

            while (_current.Kind == TokenKind.Comma) {
                Expect(TokenKind.Comma);
                labels.Add(ParseCaseLabel());
            }

            Expect(TokenKind.Colon);

            var body = new List<Statement>();
            while (!IsStartOfNextCaseArm() && _current.Kind != TokenKind.Else && _current.Kind != TokenKind.EndCase) {
                body.Add(ParseStatement());
            }

            arms.Add(new CaseArm(labels, body));
        }

        if (_current.Kind == TokenKind.Else) {
            Expect(TokenKind.Else);
            elseStmts = new List<Statement>();

            while (_current.Kind != TokenKind.EndCase) {
                elseStmts.Add(ParseStatement());
            }
        }

        Expect(TokenKind.EndCase);
        Expect(TokenKind.Semicolon);

        return new CaseStatement(selector, arms, elseStmts);
    }

    /// <summary>
    /// Parses a single CASE label integer.
    /// </summary>
    private int ParseCaseLabel() {
        var tok = Expect(TokenKind.Number);

        if (!int.TryParse(tok.Text, out var v)) {
            throw new Exception($"Invalid CASE label: {tok.Text}");
        }

        return v;
    }

    /// <summary>
    /// Returns true if the current token begins a new CASE arm.
    /// </summary>
    private bool IsStartOfNextCaseArm() {
        if (_current.Kind != TokenKind.Number) {
            return false;
        }

        return _next.Kind == TokenKind.Colon || _next.Kind == TokenKind.Comma;
    }
}

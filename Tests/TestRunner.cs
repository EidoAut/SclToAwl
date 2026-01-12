using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SclToAwl.Awl;
using SclToAwl.Lowering;
using SclParser = SclToAwl.Parser.Parser;

namespace SclToAwl.Tests;

/// <summary>
/// File-based test runner for SCL -> AWL conversions.
/// </summary>
public static class TestRunner {
    /// <summary>
    /// Runs all <c>*.scl</c> tests in <paramref name="testsDirectory"/> and compares against
    /// optional <c>&lt;name&gt;.expected.awl</c> files.
    /// </summary>
    public static int Run(string testsDirectory, bool normalizeOutput = true) {
        if (!Directory.Exists(testsDirectory)) {
            Console.Error.WriteLine($"Tests directory not found: {testsDirectory}");
            return 2;
        }

        var sclFiles = Directory.GetFiles(testsDirectory, "*.scl", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sclFiles.Count == 0) {
            Console.Error.WriteLine($"No .scl files found in: {testsDirectory}");
            return 2;
        }

        var passed = 0;
        var failed = 0;

        foreach (var sclPath in sclFiles) {
            var name = Path.GetFileNameWithoutExtension(sclPath);
            var expectedPath = Path.Combine(testsDirectory, $"{name}.expected.awl");
            var hasExpected = File.Exists(expectedPath);

            try {
                var actual = ConvertFile(sclPath);

                if (!hasExpected) {
                    Fail($"[{name}] Expected failure, but conversion succeeded.");
                    failed++;
                    continue;
                }

                var expected = File.ReadAllText(expectedPath);

                if (normalizeOutput) {
                    actual = Normalize(actual);
                    expected = Normalize(expected);
                }

                if (string.Equals(actual, expected, StringComparison.Ordinal)) {
                    Pass($"[{name}] PASS");
                    passed++;
                } else {
                    Fail($"[{name}] FAIL - Output mismatch");
                    PrintDiffHint(expectedPath, actual, expected, normalizeOutput);
                    failed++;
                }
            } catch (Exception ex) {
                if (!hasExpected) {
                    Pass($"[{name}] PASS (failed as expected: {ex.GetType().Name})");
                    passed++;
                } else {
                    Fail($"[{name}] FAIL - Threw {ex.GetType().Name}: {ex.Message}");
                    failed++;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {passed} passed, {failed} failed, {sclFiles.Count} total");

        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Converts a single <c>.scl</c> file to AWL using "convert mode" defaults.
    /// </summary>
    private static string ConvertFile(string sclPath) {
        var scl = File.ReadAllText(sclPath);
        var ast = new SclParser(scl).ParseProgram();

        // Tests default to "convert mode" (ladderOnly: false) for broader language coverage.
        var options = new LoweringOptions(
            ladderOnly: false,
            debugView: false,
            loopIterationsPerScan: 1,
            maxDynamicArrayIndex: 7,
            prefix: ""
        );

        var ir = new Lowerer(options).Lower(ast);
        return new AwlEmitter().Emit(ir);
    }

    /// <summary>
    /// Prints a passing result in green.
    /// </summary>
    private static void Pass(string msg) {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    /// <summary>
    /// Prints a failing result in red.
    /// </summary>
    private static void Fail(string msg) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(msg);
        Console.ResetColor();
    }

    /// <summary>
    /// Normalizes text for stable comparisons (LF, trimmed trailing whitespace, no leading/trailing blank lines).
    /// </summary>
    private static string Normalize(string s) {
        var lines = s.Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// Prints a small "first differing line" hint to help debug mismatches.
    /// </summary>
    private static void PrintDiffHint(string expectedPath, string actual, string expected, bool normalized) {
        Console.WriteLine($"  Expected file: {expectedPath}");
        Console.WriteLine($"  Comparison: {(normalized ? "normalized" : "raw")}");

        var aLines = actual.Split('\n');
        var eLines = expected.Split('\n');
        var max = Math.Max(aLines.Length, eLines.Length);

        for (var i = 0; i < max; i++) {
            var a = i < aLines.Length ? aLines[i] : "<EOF>";
            var e = i < eLines.Length ? eLines[i] : "<EOF>";

            if (!string.Equals(a, e, StringComparison.Ordinal)) {
                Console.WriteLine($"  First diff at line {i + 1}:");
                Console.WriteLine($"    expected: {e}");
                Console.WriteLine($"    actual  : {a}");
                break;
            }
        }
    }
}

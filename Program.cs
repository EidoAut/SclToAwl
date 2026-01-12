/*
 * =============================================================================
 *  SclToAwl
 * =============================================================================
 *  SclToAwl is a command-line tool that converts a ladder-friendly subset of
 *  Siemens SCL (Structured Control Language) into AWL.
 *
 *  The primary goal is to generate clean, readable, and ladder-oriented AWL
 *  code that can be easily understood and maintained in Siemens environments
 *  such as TIA Portal and STEP7.
 *
 *  ---------------------------------------------------------------------------
 *  Internal pipeline
 *  ---------------------------------------------------------------------------
 *    Lexer  →  Parser  →  AST  →  Lowering (IR)  →  AWL Emitter
 *
 *  ---------------------------------------------------------------------------
 *  Conversion modes
 *  ---------------------------------------------------------------------------
 *    • Strict (ladder-only)
 *        Emits only ladder-clean logic. No temporaries, jumps, or flow control
 *        constructs are allowed. If a construct cannot be expressed as pure
 *        ladder logic, the conversion fails.
 *
 *    • Convert
 *        More permissive mode. Allows the use of temporaries, conditional jumps,
 *        bounded loops, and index unrolling when required, while still
 *        preferring ladder-style emission whenever possible.
 *
 *  ---------------------------------------------------------------------------
 *  Credits
 *  ---------------------------------------------------------------------------
 *    Samuel Seijo
 *    Programmer at EIDO Automation
 *
 *  ---------------------------------------------------------------------------
 *  License
 *  ---------------------------------------------------------------------------
 *    MIT License — see the LICENSE file for details.
 * =============================================================================
 */

using System;
using System.IO;
using System.Linq;
using SclToAwl.Awl;
using SclToAwl.Lowering;
using SclToAwl.Parser;
using SclToAwl.Tests;

/// <summary>
/// CLI entry point for the SclToAwl tool.
/// </summary>
public static class Program {
    private const int ExitSuccess = 0;
    private const int ExitUsageError = 1;
    private const int ExitIoError = 2;
    private const int ExitRuntimeError = 3;

    /// <summary>
    /// Program entry point.
    /// </summary>
    public static int Main(string[] args) {
        PrintBanner();

        var appName = GetAppName();

        if (args.Length == 0 || IsHelpToken(args[0])) {
            PrintRootHelp(appName);
            return ExitSuccess;
        }

        if (IsVersionToken(args[0])) {
            PrintVersion();
            return ExitSuccess;
        }

        var command = args[0].ToLowerInvariant();
        var tail = args.Skip(1).ToArray();

        try {
            return command switch {
                "convert" => RunConvert(appName, tail),
                "network" => RunNetwork(appName, tail),
                "test"    => RunTests(appName, tail),
                "help"    => RunHelp(appName, tail),
                _         => UnknownCommand(appName, command)
            };
        } catch (FileNotFoundException ex) {
            Console.Error.WriteLine(ex.Message);
            return ExitIoError;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitRuntimeError;
        }
    }

    /// <summary>
    /// Returns the executable name used in help output.
    /// </summary>
    private static string GetAppName() {
        var argv0 = Environment.GetCommandLineArgs().FirstOrDefault();
        return Path.GetFileNameWithoutExtension(argv0 ?? "scltoawl");
    }

    /// <summary>
    /// Handles the <c>help</c> command.
    /// </summary>
    private static int RunHelp(string appName, string[] args) {
        if (args.Length == 0) {
            PrintRootHelp(appName);
            return ExitSuccess;
        }

        return args[0].ToLowerInvariant() switch {
            "convert" => PrintConvertHelp(appName),
            "network" => PrintNetworkHelp(appName),
            "test"    => PrintTestHelp(appName),
            _         => UnknownCommand(appName, args[0])
        };
    }

    /// <summary>
    /// Handles the <c>test</c> command.
    /// </summary>
    private static int RunTests(string appName, string[] args) {
        if (args.Any(IsHelpToken)) {
            return PrintTestHelp(appName);
        }

        var testsDir = Path.Combine(Environment.CurrentDirectory, "Tests");
        var normalize = true;

        for (var i = 0; i < args.Length; i++) {
            var a = args[i];

            if (a is "--raw") {
                normalize = false;
                continue;
            }

            if (a is "-p" or "--path") {
                testsDir = RequireValue(args, ref i, a);
                continue;
            }

            // Back-compat: `test <path>`
            if (!a.StartsWith("-", StringComparison.Ordinal) && i == 0) {
                testsDir = a;
                continue;
            }

            return UsageError(appName, $"Unknown option for 'test': {a}", "test");
        }

        return TestRunner.Run(testsDir, normalizeOutput: normalize);
    }

    /// <summary>
    /// Handles the <c>convert</c> command.
    /// </summary>
    private static int RunConvert(string appName, string[] args) {
        if (args.Length == 0 || args.Any(IsHelpToken)) {
            return PrintConvertHelp(appName);
        }

        var options = ParseTranspileOptions(appName, args, commandName: "convert", out var exitCode);
        if (options is null) return exitCode;

        return RunBatchTranspile(
            appName,
            options,
            emitInline: false
        );
    }

    /// <summary>
    /// Handles the <c>network</c> command (emit AWL without NETWORK/TITLE headers).
    /// </summary>
    private static int RunNetwork(string appName, string[] args) {
        if (args.Length == 0 || args.Any(IsHelpToken)) {
            return PrintNetworkHelp(appName);
        }

        var options = ParseTranspileOptions(appName, args, commandName: "network", out var exitCode);
        if (options is null) return exitCode;

        return RunBatchTranspile(
            appName,
            options,
            emitInline: true
        );
    }

    /// <summary>
    /// Parses and validates <c>convert</c> options.
    /// </summary>
    private static TranspileOptions? ParseTranspileOptions(string appName, string[] args, string commandName, out int exitCode) {
        exitCode = ExitUsageError;

        var inputArgs = new System.Collections.Generic.List<string>();
        string? outputPath = null;   // used for concat/single-output modes
        string? outDir = null;
        var strict = true;
        var debug = false;
        var loopSteps = 1;
        var dynIndexMax = 7;
        var readStdin = false;
        var writeStdout = false;
        var concat = false;
        var overwrite = false;
        var suffix = "";
        var prefix = "";
        var prefixFromName = false;
        var addConcatHeaders = true;

        for (var i = 0; i < args.Length; i++) {
            var a = args[i];

            // Positional input(s)
            if (!a.StartsWith("-", StringComparison.Ordinal)) {
                inputArgs.Add(a);
                continue;
            }

            switch (a) {
                case "-i":
                case "--input":
                    inputArgs.Add(RequireValue(args, ref i, a));
                    break;
                case "-o":
                case "--output":
                    outputPath = RequireValue(args, ref i, a);
                    break;
                case "--outdir":
                    outDir = RequireValue(args, ref i, a);
                    break;
                case "--stdin":
                    readStdin = true;
                    break;
                case "--stdout":
                    writeStdout = true;
                    break;
                case "--concat":
                    concat = true;
                    break;
                case "--overwrite":
                    overwrite = true;
                    break;
                case "--suffix":
                    suffix = RequireValue(args, ref i, a);
                    break;
                case "--prefix":
                    prefix = RequireValue(args, ref i, a);
                    break;
                case "--prefix-from-name":
                    prefixFromName = true;
                    break;
                case "--no-concat-headers":
                    addConcatHeaders = false;
                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--convert":
                    strict = false;
                    break;
                case "--debug":
                    debug = true;
                    break;
                case "--loop-steps":
                    loopSteps = ParseInt(RequireValue(args, ref i, a), 1, 1, a);
                    break;
                case "--dyn-index-max":
                    dynIndexMax = ParseInt(RequireValue(args, ref i, a), 0, 7, a);
                    break;
                default:
                    exitCode = UsageError(appName, $"Unknown option for '{commandName}': {a}", commandName);
                    return null;
            }
        }

        if (readStdin && inputArgs.Count > 0) {
            exitCode = UsageError(appName, "Use either <input.scl> (one or more) or --stdin, not both.", commandName);
            return null;
        }

        if (!readStdin && inputArgs.Count == 0) {
            exitCode = UsageError(appName, "Missing input file.", commandName);
            return null;
        }

        if (writeStdout && outputPath is not null) {
            exitCode = UsageError(appName, "Use either --stdout or --output, not both.", commandName);
            return null;
        }

        if (readStdin) {
            // stdin is always single-input.
            if (!writeStdout && outputPath is null) {
                exitCode = UsageError(appName, "When using --stdin you must specify --stdout or --output.", commandName);
                return null;
            }
        } else {
            // file inputs
            if (inputArgs.Count > 1 && writeStdout && !concat) {
                exitCode = UsageError(appName, "--stdout with multiple inputs requires --concat.", commandName);
                return null;
            }

            if (inputArgs.Count > 1 && outputPath is not null && !concat) {
                exitCode = UsageError(appName, "--output with multiple inputs requires --concat (otherwise use --outdir).", commandName);
                return null;
            }

            if (concat) {
                if (!writeStdout && outputPath is null) {
                    exitCode = UsageError(appName, "When using --concat you must specify --stdout or --output.", commandName);
                    return null;
                }
            }
        }

        return new TranspileOptions(
            InputArgs: inputArgs,
            OutputPath: outputPath,
            OutDir: outDir,
            ReadFromStdin: readStdin,
            WriteToStdout: writeStdout,
            Concat: concat,
            AddConcatHeaders: addConcatHeaders,
            Overwrite: overwrite,
            Suffix: suffix,
            Strict: strict,
            Debug: debug,
            LoopSteps: loopSteps,
            DynIndexMax: dynIndexMax,
            Prefix: prefix,
            PrefixFromName: prefixFromName
        );
    }

    /// <summary>
    /// Reads a file from disk.
    /// </summary>
    private static string ReadInputFile(string inputPath) {
        if (!File.Exists(inputPath)) {
            throw new FileNotFoundException($"Input not found: {inputPath}");
        }

        return File.ReadAllText(inputPath);
    }

    /// <summary>
    /// Runs the parse/lower/emit pipeline.
    /// </summary>
    private static string TranspileScl(string scl, TranspileOptions options, string effectivePrefix, bool emitInline) {
        var ast = new Parser(scl).ParseProgram();

        var loweringOptions = new LoweringOptions(
            options.Strict,
            options.Debug,
            options.LoopSteps,
            options.DynIndexMax,
            prefix: effectivePrefix
        );

        var ir = new Lowerer(loweringOptions).Lower(ast);
        var emitter = new AwlEmitter();
        return emitInline ? emitter.EmitInline(ir) : emitter.Emit(ir);
    }

    /// <summary>
    /// Runs a single or multi-file transpilation with optional concatenation.
    /// </summary>
    private static int RunBatchTranspile(string appName, TranspileOptions options, bool emitInline) {
        if (options.ReadFromStdin) {
            var scl = Console.In.ReadToEnd();
            var awl = TranspileScl(scl, options, effectivePrefix: options.Prefix, emitInline: emitInline);
            if (options.WriteToStdout) {
                Console.Out.Write(awl);
                return ExitSuccess;
            }

            WriteFileEnsuringDir(options.OutputPath!, awl, overwrite: options.Overwrite);
            Console.WriteLine($"Wrote: {options.OutputPath}");
            return ExitSuccess;
        }

        var inputPaths = ExpandInputs(options.InputArgs);
        if (inputPaths.Count == 0) {
            Console.Error.WriteLine("No input files found.");
            return ExitIoError;
        }

        if (options.Concat) {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < inputPaths.Count; i++) {
                var p = inputPaths[i];
                var scl = ReadInputFile(p);
                var effPrefix = BuildEffectivePrefix(options, Path.GetFileNameWithoutExtension(p));
                var awl = TranspileScl(scl, options, effPrefix, emitInline);

                if (options.AddConcatHeaders) {
                    sb.AppendLine($"; ===== {Path.GetFileName(p)} =====");
                }

                sb.Append(awl);

                if (i != inputPaths.Count - 1) {
                    sb.AppendLine();
                }
            }

            var all = sb.ToString();
            if (options.WriteToStdout) {
                Console.Out.Write(all);
                return ExitSuccess;
            }

            WriteFileEnsuringDir(options.OutputPath!, all, overwrite: options.Overwrite);
            Console.WriteLine($"Wrote: {options.OutputPath}");
            return ExitSuccess;
        }

        // Per-file output
        foreach (var p in inputPaths) {
            var scl = ReadInputFile(p);
            var effPrefix = BuildEffectivePrefix(options, Path.GetFileNameWithoutExtension(p));
            var awl = TranspileScl(scl, options, effPrefix, emitInline);

            var outPath = BuildOutputPathForInput(p, options);
            WriteFileEnsuringDir(outPath, awl, overwrite: options.Overwrite);
            Console.WriteLine($"Wrote: {outPath}");
        }

        return ExitSuccess;
    }

    private static string BuildOutputPathForInput(string inputPath, TranspileOptions options) {
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var dir = options.OutDir ?? Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        var file = baseName + (options.Suffix ?? "") + ".awl";
        return Path.Combine(dir, file);
    }

    private static void WriteFileEnsuringDir(string path, string content, bool overwrite) {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(path) && !overwrite) {
            throw new Exception($"Output exists (use --overwrite): {path}");
        }

        File.WriteAllText(path, content);
    }

    private static string BuildEffectivePrefix(TranspileOptions options, string inputBaseName) {
        var p = options.Prefix ?? "";
        if (options.PrefixFromName) {
            p += SanitizePrefixToken(inputBaseName) + "_";
        }

        return p;
    }

    private static string SanitizePrefixToken(string s) {
        if (string.IsNullOrEmpty(s)) return "_";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s) {
            if (char.IsLetterOrDigit(ch) || ch == '_') {
                sb.Append(ch);
            } else {
                sb.Append('_');
            }
        }

        var outName = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(outName) ? "_" : outName;
    }

    private static System.Collections.Generic.List<string> ExpandInputs(System.Collections.Generic.List<string> inputArgs) {
        var results = new System.Collections.Generic.List<string>();

        foreach (var arg in inputArgs) {
            // Directory => all *.scl
            if (Directory.Exists(arg)) {
                results.AddRange(Directory.GetFiles(arg, "*.scl", SearchOption.TopDirectoryOnly));
                continue;
            }

            // Wildcards
            if (arg.Contains('*') || arg.Contains('?')) {
                var dir = Path.GetDirectoryName(arg);
                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
                var pattern = Path.GetFileName(arg);
                results.AddRange(Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly));
                continue;
            }

            results.Add(arg);
        }

        // Normalize and de-dup preserving order
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var final = new System.Collections.Generic.List<string>();
        foreach (var p in results) {
            var full = Path.GetFullPath(p);
            if (seen.Add(full)) {
                final.Add(full);
            }
        }

        final.Sort(StringComparer.OrdinalIgnoreCase);
        return final;
    }

    /// <summary>
    /// Prints an unknown command error and root help.
    /// </summary>
    private static int UnknownCommand(string appName, string command) {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        PrintRootHelp(appName);
        return ExitUsageError;
    }

    /// <summary>
    /// Prints an error plus the relevant help section.
    /// </summary>
    private static int UsageError(string appName, string message, string? command = null) {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();

        if (command is "convert") {
            PrintConvertHelp(appName);
        } else if (command is "network") {
            PrintNetworkHelp(appName);
        } else if (command is "test") {
            PrintTestHelp(appName);
        } else {
            PrintRootHelp(appName);
        }

        return ExitUsageError;
    }

    /// <summary>
    /// Requires a value after an option and advances <paramref name="i"/>.
    /// </summary>
    private static string RequireValue(string[] args, ref int i, string option) {
        if (i + 1 >= args.Length) {
            throw new Exception($"Missing value for {option}.");
        }

        i++;
        return args[i];
    }

    /// <summary>
    /// Parses an integer option with validation and fallback.
    /// </summary>
    private static int ParseInt(string value, int min, int fallback, string name) {
        if (!int.TryParse(value, out var n) || n < min) {
            Console.Error.WriteLine($"Warning: invalid value for {name}: '{value}'. Using {fallback}.");
            return fallback;
        }

        return n;
    }

    /// <summary>
    /// Returns true if the token is a help flag.
    /// </summary>
    private static bool IsHelpToken(string a) {
        return a is "-h" or "--help" or "help" or "-?";
    }

    /// <summary>
    /// Returns true if the token is a version flag.
    /// </summary>
    private static bool IsVersionToken(string a) {
        return a is "-v" or "--version";
    }

    /// <summary>
    /// Prints assembly version.
    /// </summary>
    private static void PrintVersion() {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        Console.WriteLine(version);
    }

    /// <summary>
    /// Prints top-level help.
    /// </summary>
    private static void PrintRootHelp(string appName) {
        Console.WriteLine($"{appName} - SCL to AWL converter (ladder-focused)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  {appName} <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  convert    Convert an .scl file (or stdin) to AWL");
        Console.WriteLine("  network    Convert a single-network SCL snippet to AWL (no NETWORK/TITLE headers)");
        Console.WriteLine("  test       Run file-based conversion tests");
        Console.WriteLine("  help       Show help for a command");
        Console.WriteLine();
        Console.WriteLine("Global options:");
        Console.WriteLine("  -h, --help       Show help");
        Console.WriteLine("  -v, --version    Show version");
    }

    /// <summary>
    /// Prints convert help.
    /// </summary>
    private static int PrintConvertHelp(string appName) {
        Console.WriteLine("Convert SCL to AWL");
        Console.WriteLine();
        Console.WriteLine($"  {appName} convert <input.scl> [more.scl ...] [options]");
        Console.WriteLine($"  {appName} convert <dir> [options]              (converts *.scl in the directory)");
        Console.WriteLine($"  {appName} convert <glob> [options]             (e.g. Networks/*.scl)");
        Console.WriteLine($"  {appName} convert --stdin (--stdout | --output <file>)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --strict / --convert        Select strict (ladder-only) or convert mode");
        Console.WriteLine("  --outdir <dir>              Write outputs to this directory (per-file mode)");
        Console.WriteLine("  --suffix <text>             Append suffix before .awl (per-file mode)");
        Console.WriteLine("  --concat                    Concatenate all outputs to --output/--stdout");
        Console.WriteLine("  --no-concat-headers         Disable file header comments in concat output");
        Console.WriteLine("  --overwrite                 Overwrite existing output files");
        Console.WriteLine("  --prefix <text>             Prefix for generated temps/labels");
        Console.WriteLine("  --prefix-from-name          Add per-input prefix based on file name");
        return ExitSuccess;
    }

    /// <summary>
    /// Prints network help.
    /// </summary>
    private static int PrintNetworkHelp(string appName) {
        Console.WriteLine("Convert a single-network SCL snippet to AWL (inline)");
        Console.WriteLine();
        Console.WriteLine($"  {appName} network <input.scl> [more.scl ...] [options]");
        Console.WriteLine($"  {appName} network <dir> [options]");
        Console.WriteLine($"  {appName} network <glob> [options]");
        Console.WriteLine($"  {appName} network --stdin (--stdout | --output <file>)");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Output contains no NETWORK/TITLE headers; it's intended to be pasted inside a network.");
        Console.WriteLine("  - Use --prefix / --prefix-from-name to avoid label/temp collisions when combining outputs.");
        Console.WriteLine();
        Console.WriteLine("Options: (same as 'convert')");
        Console.WriteLine("  --strict / --convert        Select strict (ladder-only) or convert mode");
        Console.WriteLine("  --outdir <dir>              Write outputs to this directory (per-file mode)");
        Console.WriteLine("  --suffix <text>             Append suffix before .awl (per-file mode)");
        Console.WriteLine("  --concat                    Concatenate all outputs to --output/--stdout");
        Console.WriteLine("  --no-concat-headers         Disable file header comments in concat output");
        Console.WriteLine("  --overwrite                 Overwrite existing output files");
        Console.WriteLine("  --prefix <text>             Prefix for generated temps/labels");
        Console.WriteLine("  --prefix-from-name          Add per-input prefix based on file name");
        return ExitSuccess;
    }

    /// <summary>
    /// Prints test help.
    /// </summary>
    private static int PrintTestHelp(string appName) {
        Console.WriteLine("Run tests");
        Console.WriteLine();
        Console.WriteLine($"  {appName} test [path] [options]");
        return ExitSuccess;
    }

    /// <summary>
    /// Prints the startup banner.
    /// </summary>
    private static void PrintBanner() {
        if (Console.IsOutputRedirected) {
            return;
        }

        Console.WriteLine(@"
 _________      ._____________       _____         .__   
 /   _____/ ____ |  \__    ___/___   /  _  \__  _  _|  |  
 \_____  \_/ ___\|  | |    | /  _ \ /  /_\  \ \/ \/ /  |  
 /        \  \___|  |_|    |(  <_> )    |    \     /|  |__
/_______  /\___  >____/____| \____/\____|__  /\/\_/ |____/
        \/     \/                          \/             

SCL -> AWL like it's 1998 again.
");
    }

    /// <summary>
    /// Options shared by convert/network, including batch processing.
    /// </summary>
    private sealed record TranspileOptions(
        System.Collections.Generic.List<string> InputArgs,
        string? OutputPath,
        string? OutDir,
        bool ReadFromStdin,
        bool WriteToStdout,
        bool Concat,
        bool AddConcatHeaders,
        bool Overwrite,
        string Suffix,
        bool Strict,
        bool Debug,
        int LoopSteps,
        int DynIndexMax,
        string Prefix,
        bool PrefixFromName
    );
}

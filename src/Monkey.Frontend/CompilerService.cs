using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using System.Text.RegularExpressions;
using Monkey.AST;
using Monkey.AST.Visualization;
using Monkey.AST.Statements;
using Monkey.CodeGeneration;
using Monkey.CodeGeneration.IR;
using Monkey.SymbolTable;

namespace Monkey.Frontend
{
    public static class CompilerService
    {
        private class SyntaxErrorListener : BaseErrorListener
        {
            private readonly List<string> _errors;
            public SyntaxErrorListener(List<string> errors) => _errors = errors;
            public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
            {
                var tokenText = offendingSymbol?.Text ?? "<EOF>";

                // Heurísticas para mensajes más amigables
                string friendly = msg;
                try
                {
                    // mismatched input 'X' expecting 'Y'
                    var m1 = Regex.Match(msg ?? string.Empty, "mismatched input '\\'?(.*?)\\''? expecting (.*)", RegexOptions.IgnoreCase);
                    if (!m1.Success)
                        m1 = Regex.Match(msg ?? string.Empty, "mismatched input (?:'|\")(.*?)(?:'|\") expecting (.*)", RegexOptions.IgnoreCase);

                    if (m1.Success)
                    {
                        var found = m1.Groups[1].Value.Trim();
                        var expect = m1.Groups[2].Value.Trim();
                        // extract a single expected token if present in quotes
                        var mTok = Regex.Match(expect, "(?:'|\")?(.*?)(?:'|\")?$");
                        var expToken = mTok.Success ? mTok.Groups[1].Value : expect;
                        friendly = $"Se esperaba '{expToken}' pero se encontró '{found}' (línea {line}, col {charPositionInLine}). Posible falta de '{expToken}'.";
                    }
                    else
                    {
                        // extraneous input 'X' expecting Y
                        var m2 = Regex.Match(msg ?? string.Empty, "extraneous input (?:'|\")(.*?)(?:'|\") expecting (.*)", RegexOptions.IgnoreCase);
                        if (m2.Success)
                        {
                            var unexpected = m2.Groups[1].Value.Trim();
                            var expect = m2.Groups[2].Value.Trim();
                            friendly = $"Token inesperado '{unexpected}' en línea {line}, col {charPositionInLine}. Se esperaba {expect}.";
                        }
                        else
                        {
                            // no viable alternative at input 'X'
                            var m3 = Regex.Match(msg ?? string.Empty, "no viable alternative at input (?:'|\")(.*?)(?:'|\")", RegexOptions.IgnoreCase);
                            if (m3.Success)
                            {
                                var near = m3.Groups[1].Value.Trim();
                                friendly = $"Construcción sintáctica inválida cerca de '{near}' en línea {line}, col {charPositionInLine}.";
                            }
                        }
                    }
                }
                catch
                {
                    // fall back to original message
                    friendly = msg;
                }

                _errors.Add($"Sintaxis: {friendly} (token: '{tokenText}')");
            }
        }

        public static (List<string> errors, string output, IReadOnlyList<string> instructions) CompileAndRun(string source)
        {
            var errors = new List<string>();

            // 1) Parse and collect syntax errors
            var input = new AntlrInputStream(source);
            var lexer = new MonkeyLexer(input);
            var tokens = new CommonTokenStream(lexer);
            var parser = new MonkeyParser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new SyntaxErrorListener(errors));

            MonkeyParser.ProgramContext tree = null!;
            try
            {
                tree = parser.program();
            }
            catch (Exception ex)
            {
                errors.Add("Parser error: " + ex.Message);
            }

            if (errors.Count > 0)
            {
                // Build structured output so frontend GUI can always parse sections
                var finalOutputErr = new System.Text.StringBuilder();
                finalOutputErr.AppendLine("=== Errores ===");
                foreach (var e in errors) finalOutputErr.AppendLine(e);
                finalOutputErr.AppendLine("=== Salida ===");
                finalOutputErr.AppendLine();
                finalOutputErr.AppendLine("=== IR ===");
                // no IR when parsing failed
                return (errors, finalOutputErr.ToString(), Array.Empty<string>());
            }

            // 2) Build AST
            var astBuilder = new ASTBuilderVisitor();
            var astNode = astBuilder.Visit(tree);
            var program = astNode as Monkey.AST.Program ?? throw new InvalidOperationException("AST no construido");
            var astPrinter = new ASTPrinterVisitor();
            var astText = astPrinter.Print(astNode);
            // 3) Symbol table
            var symBuilder = new SymbolTableBuilderVisitor();
            symBuilder.VisitNode(program);

            // 4) Type checking
            var typeChecker = new Monkey.TypeChecking.TypeCheckerVisitor(symBuilder);
            typeChecker.VisitNode(program);
            var typeErrors = typeChecker.GetErrors();
            if (typeErrors.Count > 0)
            {
                errors.AddRange(typeErrors);
                var finalOutputErr = new System.Text.StringBuilder();
                finalOutputErr.AppendLine("=== Errores ===");
                foreach (var e in errors) finalOutputErr.AppendLine(e);
                finalOutputErr.AppendLine("=== Salida ===");
                finalOutputErr.AppendLine();
                finalOutputErr.AppendLine("=== IR ===");
                return (errors, finalOutputErr.ToString(), Array.Empty<string>());
            }

            // 5) Code generation
            var codeGen = new CodeGeneratorVisitor();
            codeGen.VisitNode(program);
            var instructions = codeGen.Instructions.ToList();

            // Ensure main is called (as Program.cs did)
            var runInstr = new List<string>(instructions);
            if (runInstr.Count == 0 || !runInstr[0].StartsWith("CALL"))
                runInstr.Insert(0, "CALL main 0");

            // 6) Execute and capture output (redirect Console)
            var sw = new StringWriter();
            var origOut = Console.Out;
            try
            {
                Console.SetOut(sw);

                // IR textual interpreter
                try
                {
                    var irInterp = new IRInterpreter();
                    irInterp.Execute(runInstr);
                }
                catch (Exception ex)
                {
                    // Try to map the failing IR index to a source line using the emitted instructions.
                    string add = ex.Message;
                    var m = Regex.Match(ex.Message ?? string.Empty, @"\b(?:line|ip)\s+(\d+)\b", RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var ip))
                    {
                        int srcLine = -1;
                        for (int k = Math.Min(ip, runInstr.Count - 1); k >= 0; k--)
                        {
                            var s = runInstr[k];
                            if (s != null && s.StartsWith("// LINE "))
                            {
                                var mm = Regex.Match(s, @"//\s*LINE\s+(\d+)");
                                if (mm.Success && int.TryParse(mm.Groups[1].Value, out var l)) { srcLine = l; break; }
                            }
                        }
                        if (srcLine != -1)
                        {
                            // Include the source line snippet to help the GUI highlight the exact line
                            try
                            {
                                var srcLines = source?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None) ?? Array.Empty<string>();
                                string snippet = srcLines.Length >= srcLine ? srcLines[srcLine - 1].Trim() : string.Empty;
                                if (!string.IsNullOrEmpty(snippet))
                                    add += $" -> source line {srcLine}: \"{snippet}\"";
                                else
                                    add += $" -> source line {srcLine}";
                            }
                            catch
                            {
                                add += $" -> source line {srcLine}";
                            }
                        }
                    }
                    errors.Add("IR execution error: " + add);
                }

                // VM
                try
                {
                    var vm = new VirtualMachine(runInstr);
                    vm.Execute();
                }
                catch (Exception ex)
                {
                    string add = ex.Message;
                    var m = Regex.Match(ex.Message ?? string.Empty, @"\bip\s+(\d+)\b", RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var ip))
                    {
                        int srcLine = -1;
                        for (int k = Math.Min(ip, runInstr.Count - 1); k >= 0; k--)
                        {
                            var s = runInstr[k];
                            if (s != null && s.StartsWith("// LINE "))
                            {
                                var mm = Regex.Match(s, @"//\s*LINE\s+(\d+)");
                                if (mm.Success && int.TryParse(mm.Groups[1].Value, out var l)) { srcLine = l; break; }
                            }
                        }
                        if (srcLine != -1)
                        {
                            try
                            {
                                var srcLines = source?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None) ?? Array.Empty<string>();
                                string snippet = srcLines.Length >= srcLine ? srcLines[srcLine - 1].Trim() : string.Empty;
                                if (!string.IsNullOrEmpty(snippet))
                                    add += $" -> source line {srcLine}: \"{snippet}\"";
                                else
                                    add += $" -> source line {srcLine}";
                            }
                            catch
                            {
                                add += $" -> source line {srcLine}";
                            }
                        }
                    }
                    errors.Add("VM execution error: " + add);
                }
            }
            finally
            {
                Console.SetOut(origOut);
            }

            // Build output with explicit sections that the GUI expects so it can parse them.
            // Sections: === Errores ===, === Salida ===, === IR ===
            var finalOutput = new System.Text.StringBuilder();

            finalOutput.AppendLine("=== Errores ===");
            if (errors.Count > 0)
            {
                foreach (var e in errors) finalOutput.AppendLine(e);
            }

            finalOutput.AppendLine("=== Salida ===");
            // Include a brief AST header in the Salida to help CLI users, then the program output
            finalOutput.AppendLine($"🔍 AST: {program}");
            finalOutput.AppendLine();
            finalOutput.Append(sw.ToString());

            finalOutput.AppendLine();
            finalOutput.AppendLine("=== IR ===");
            // Print the IR textual instructions (one per line)
            foreach (var instr in instructions)
                finalOutput.AppendLine(instr);

            return (errors, finalOutput.ToString(), instructions);
        }

        // Generate DOT representation of the AST for graphical visualization.
        // Returns a tuple of (errors, dotText). If parsing/build fails, errors will be populated and dotText empty.
        public static (List<string> errors, string dot) GenerateAstDot(string source)
        {
            var errors = new List<string>();

            var input = new AntlrInputStream(source);
            var lexer = new MonkeyLexer(input);
            var tokens = new CommonTokenStream(lexer);
            var parser = new MonkeyParser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new SyntaxErrorListener(errors));

            MonkeyParser.ProgramContext tree = null!;
            try
            {
                tree = parser.program();
            }
            catch (Exception ex)
            {
                errors.Add("Parser error: " + ex.Message);
            }

            if (errors.Count > 0)
            {
                return (errors, string.Empty);
            }

            var astBuilder = new ASTBuilderVisitor();
            var astNode = astBuilder.Visit(tree);
            var program = astNode as Monkey.AST.Program ?? throw new InvalidOperationException("AST no construido");

            var gen = new AstDotGenerator();
            var dot = gen.Generate(program);
            return (errors, dot);
        }

        // Emit CIL in-memory using the CilEmitter (Reflection.Emit PoC) and run it.
        public static (List<string> errors, string output) EmitAndRunInMemory(string source)
        {
            var errors = new List<string>();

            var input = new AntlrInputStream(source);
            var lexer = new MonkeyLexer(input);
            var tokens = new CommonTokenStream(lexer);
            var parser = new MonkeyParser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new SyntaxErrorListener(errors));

            MonkeyParser.ProgramContext tree = null!;
            try
            {
                tree = parser.program();
            }
            catch (Exception ex)
            {
                errors.Add("Parser error: " + ex.Message);
            }

            if (errors.Count > 0)
            {
                return (errors, string.Empty);
            }

            var astBuilder = new ASTBuilderVisitor();
            var astNode = astBuilder.Visit(tree);
            var program = astNode as Monkey.AST.Program ?? throw new InvalidOperationException("AST no construido");

            // Symbol table + type checking
            var symBuilder = new SymbolTableBuilderVisitor();
            symBuilder.VisitNode(program);

            var typeChecker = new Monkey.TypeChecking.TypeCheckerVisitor(symBuilder);
            typeChecker.VisitNode(program);
            var typeErrors = typeChecker.GetErrors();
            if (typeErrors.Count > 0)
            {
                errors.AddRange(typeErrors);
                return (errors, string.Empty);
            }

            // Code generation to IR-like instructions
            var codeGen = new CodeGeneratorVisitor();
            codeGen.VisitNode(program);
            var instructions = codeGen.Instructions.ToList();

            try
            {
                var cilGen = new Monkey.CodeGeneration.CilCodeGeneratorVisitor();
                var outText = cilGen.EmitAndRun(program);
                return (errors, outText);
            }
            catch (Exception ex)
            {
                errors.Add("Emit/execute error: " + ex.Message);
                return (errors, string.Empty);
            }
        }

        // Emit a self-contained executable using C# generation + dotnet publish.
        // Returns (errors, pathToExeOrDll)
        public static (List<string> errors, string outputPath) EmitExeToDisk(string source, string outputDirectory)
        {
            var errors = new List<string>();

            var input = new AntlrInputStream(source);
            var lexer = new MonkeyLexer(input);
            var tokens = new CommonTokenStream(lexer);
            var parser = new MonkeyParser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(new SyntaxErrorListener(errors));

            MonkeyParser.ProgramContext tree = null!;
            try
            {
                tree = parser.program();
            }
            catch (Exception ex)
            {
                errors.Add("Parser error: " + ex.Message);
            }

            if (errors.Count > 0)
            {
                return (errors, string.Empty);
            }

            var astBuilder = new ASTBuilderVisitor();
            var astNode = astBuilder.Visit(tree);
            var program = astNode as Monkey.AST.Program ?? throw new InvalidOperationException("AST no construido");

            // Symbol table + type checking
            var symBuilder = new SymbolTableBuilderVisitor();
            symBuilder.VisitNode(program);

            var typeChecker = new Monkey.TypeChecking.TypeCheckerVisitor(symBuilder);
            typeChecker.VisitNode(program);
            var typeErrors = typeChecker.GetErrors();
            if (typeErrors.Count > 0)
            {
                errors.AddRange(typeErrors);
                return (errors, string.Empty);
            }

            try
            {
                var emitter = new Monkey.CodeGeneration.CSharpEmitter();
                var exePath = emitter.EmitExe(program, outputDirectory);
                return (errors, exePath);
            }
            catch (Exception ex)
            {
                errors.Add("Emit/exe error: " + ex.Message);
                return (errors, string.Empty);
            }
        }
    }
}

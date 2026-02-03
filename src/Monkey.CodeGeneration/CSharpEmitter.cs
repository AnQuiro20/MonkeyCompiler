using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Monkey.AST;
using Monkey.AST.Statements;
using Monkey.AST.Expressions;
using Monkey.Common;

namespace Monkey.CodeGeneration
{
    // Generates a C# console project from the Monkey AST and invokes `dotnet publish`
    // to produce a runnable executable on the current platform.
    public class CSharpEmitter
    {
        public string EmitExe(Monkey.AST.Program program, string outputDirectory)
        {
            // Create working folder
            Directory.CreateDirectory(outputDirectory);

            // Create project file
            var proj = GenerateCsProj();
            File.WriteAllText(Path.Combine(outputDirectory, "MonkeyGenerated.csproj"), proj);

            // Create Program.cs
            var code = GenerateProgramCode(program);
            File.WriteAllText(Path.Combine(outputDirectory, "Program.cs"), code);

            // Run dotnet publish -c Release -r <rid> --self-contained true -o ./publish
            var rid = DetermineRid();
            var publishDir = Path.Combine(outputDirectory, "publish");
            Directory.CreateDirectory(publishDir);

            var psi = new ProcessStartInfo("dotnet")
            {
                Arguments = $"publish -c Release -r {rid} --self-contained true -o \"{publishDir}\"",
                WorkingDirectory = outputDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar dotnet publish");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"dotnet publish falló:\n{stdout}\n{stderr}");

            // Find exe in publishDir
            var files = Directory.GetFiles(publishDir);
            foreach (var f in files)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && f.EndsWith(".exe")) return f;
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsExecutable(f)) return f;
            }

            // Fallback: if publish produced a single DLL, return path to dotnet-runner
            var dll = files.FirstOrDefault(x => x.EndsWith(".dll"));
            if (dll != null) return dll;

            throw new InvalidOperationException("No se encontró el ejecutable generado en la carpeta de publicación.");
        }

        private static string GenerateCsProj()
        {
                        return @"<Project Sdk=""Microsoft.NET.Sdk""> 
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net8.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>
</Project>";
        }

        private static string DetermineRid()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "linux-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx-x64";
            return "linux-x64";
        }

        private static bool IsExecutable(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return (fi.Exists && (fi.Attributes & FileAttributes.Directory) == 0 && (fi.Length > 0));
            }
            catch { return false; }
        }

        private string GenerateProgramCode(Monkey.AST.Program program)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("namespace MonkeyGenerated");
            sb.AppendLine("{");
            sb.AppendLine("    public static class Program");
            sb.AppendLine("    {");

            // Emit functions (declared)
            foreach (var decl in program.Declarations)
            {
                if (decl is FunctionDeclaration fd)
                {
                    sb.Append(GenerateFunctionCode(fd));
                }
            }

            // Detect function literals assigned to lets inside main and emit them as named functions
            var funcLits = CollectFunctionLiterals(program);
            foreach (var kv in funcLits)
            {
                sb.Append(GenerateFunctionCode(kv.Value, kv.Key));
            }

            // Emit main (if present)
            if (program.MainFunction is FunctionDeclaration mainFd)
            {
                sb.Append(GenerateFunctionCode(mainFd));
            }

            // Emit Main that calls main
            sb.AppendLine("        public static int Main(string[] args)");
            sb.AppendLine("        {");
            if (program.MainFunction != null)
            {
                sb.AppendLine("            func_main();");
            }
            sb.AppendLine("            return 0;");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string MapType(IType t)
        {
            return t switch
            {
                Monkey.AST.Types.IntType _ => "long",
                Monkey.AST.Types.StringType _ => "string",
                Monkey.AST.Types.BoolType _ => "bool",
                Monkey.AST.Types.VoidType _ => "void",
                Monkey.AST.Types.ArrayType _ => "dynamic",
                Monkey.AST.Types.HashType _ => "dynamic",
                Monkey.AST.Types.FunctionType _ => "dynamic",
                _ => "object",
            };
        }

        // Collect top-level function literals assigned to lets inside main (and nested blocks)
        private Dictionary<string, FunctionLiteral> CollectFunctionLiterals(Monkey.AST.Program program)
        {
            var res = new Dictionary<string, FunctionLiteral>();
            if (program.MainFunction == null) return res;
            void RecurseBlock(BlockStatement block)
            {
                foreach (var s in block.Statements)
                {
                    if (s is LetStatement let && let.Value is FunctionLiteral fl)
                    {
                        res[let.Identifier.Value] = fl;
                    }
                    else if (s is BlockStatement bs)
                    {
                        RecurseBlock(bs);
                    }
                    else if (s is IfStatement ifs)
                    {
                        RecurseBlock(ifs.Consequence);
                        if (ifs.Alternative != null) RecurseBlock(ifs.Alternative);
                    }
                }
            }

            if (program.MainFunction is FunctionDeclaration mainDecl)
                RecurseBlock(mainDecl.Body);
            return res;
        }

        private string GenerateFunctionCode(FunctionDeclaration fd)
        {
            var sb = new StringBuilder();
            var retType = MapType(fd.ReturnType ?? new Monkey.AST.Types.VoidType());
            var name = fd.Name.Value;
            var paramList = string.Join(", ", fd.Parameters.Select(p => MapType(p.Type) + " " + p.Name));
            sb.AppendLine($"        public static {retType} func_{name}({paramList})");
            sb.AppendLine("        {");
            // Emit body statements
            foreach (var s in fd.Body.Statements)
            {
                sb.Append(GenerateStatementCode(s, "            "));
            }
            // If void, ensure return
            if (retType == "void") sb.AppendLine("            return;\n");
            sb.AppendLine("        }");
            sb.AppendLine();
            return sb.ToString();
        }

        private string GenerateFunctionCode(FunctionLiteral fl, string name)
        {
            var sb = new StringBuilder();
            var retType = "dynamic";
            var paramList = string.Join(", ", fl.Parameters.Select(p => "dynamic " + p.Name));
            sb.AppendLine($"        public static {retType} func_{name}({paramList})");
            sb.AppendLine("        {");
            foreach (var s in fl.Body.Statements)
            {
                sb.Append(GenerateStatementCode(s, "            "));
            }
            sb.AppendLine("            return null;\n");
            sb.AppendLine("        }");
            sb.AppendLine();
            return sb.ToString();
        }

        private string GenerateStatementCode(IASTNode stmt, string indent)
        {
            switch (stmt)
            {
                case LetStatement let:
                {
                    // For complex types (array/hash/function) prefer dynamic
                    var t = MapType(let.Type ?? new Monkey.AST.Types.IntType());
                    if (let.Type is Monkey.AST.Types.ArrayType || let.Type is Monkey.AST.Types.HashType || let.Type is Monkey.AST.Types.FunctionType)
                        t = "dynamic";

                    var expr = GenerateExpressionCode(let.Value);
                    return $"{indent}{t} {let.Identifier.Value} = {expr};\n";
                }
                case PrintStatement ps:
                {
                    var expr = GenerateExpressionCode(ps.Expression);
                    return $"{indent}Console.WriteLine({expr});\n";
                }
                case ExpressionStatement es:
                {
                    var expr = GenerateExpressionCode(es.Expression);
                    return $"{indent}{expr};\n";
                }
                case ReturnStatement rs:
                {
                    var expr = GenerateExpressionCode(rs.Expression);
                    return $"{indent}return {expr};\n";
                }
                case IfStatement ifs:
                {
                    var cond = GenerateExpressionCode(ifs.Condition as IASTNode ?? throw new InvalidOperationException());
                    var sb = new StringBuilder();
                    sb.AppendLine($"{indent}if ({cond}) {{");
                    foreach (var s in ifs.Consequence.Statements) sb.Append(GenerateStatementCode(s, indent + "    "));
                    sb.AppendLine($"{indent}}}");
                    if (ifs.Alternative != null)
                    {
                        sb.AppendLine($"{indent}else {{");
                        foreach (var s in ifs.Alternative.Statements) sb.Append(GenerateStatementCode(s, indent + "    "));
                        sb.AppendLine($"{indent}}}");
                    }
                    return sb.ToString();
                }
                case WhileStatement ws:
                {
                    var cond = GenerateExpressionCode(ws.Condition as IASTNode ?? throw new InvalidOperationException());
                    var sb = new StringBuilder();
                    sb.AppendLine($"{indent}while ({cond}) {{");
                    foreach (var s in ws.Body.Statements) sb.Append(GenerateStatementCode(s, indent + "    "));
                    sb.AppendLine($"{indent}}}");
                    return sb.ToString();
                }
                case BlockStatement bs:
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(indent + "{");
                    foreach (var s in bs.Statements) sb.Append(GenerateStatementCode(s, indent + "    "));
                    sb.AppendLine(indent + "}");
                    return sb.ToString();
                }
                default:
                    return indent + "// statement not supported\n";
            }
        }

        private string GenerateExpressionCode(IASTNode expr)
        {
            switch (expr)
            {
                case IntegerLiteral il:
                    return il.Value + "L";
                case StringLiteral sl:
                    return "@\"" + sl.Value.Replace("\"", "\"\"") + "\"";
                case BooleanLiteral bl:
                    return bl.Value ? "true" : "false";
                case Identifier id:
                    return id.Value;
                case BinaryExpression be:
                {
                    var left = GenerateExpressionCode(be.Left as IASTNode ?? throw new InvalidOperationException());
                    var right = GenerateExpressionCode(be.Right as IASTNode ?? throw new InvalidOperationException());
                    var op = be.Operator switch
                    {
                        BinaryOperator.Add => "+",
                        BinaryOperator.Subtract => "-",
                        BinaryOperator.Multiply => "*",
                        BinaryOperator.Divide => "/",
                        BinaryOperator.Less => "<",
                        BinaryOperator.Greater => ">",
                        BinaryOperator.LessOrEqual => "<=",
                        BinaryOperator.GreaterOrEqual => ">=",
                        BinaryOperator.Equal => "==",
                        _ => throw new NotSupportedException("Operator not supported")
                    };
                    return $"({left} {op} {right})";
                }
                case CallExpression call:
                {
                    var args = string.Join(", ", call.Arguments.Select(a => GenerateExpressionCode(a as IASTNode ?? throw new InvalidOperationException())));
                    return $"func_{call.FunctionName}({args})";
                }
                default:
                    throw new NotSupportedException($"Expresión no soportada por C# emitter: {expr.GetType().Name}");
            }
        }
    }
}

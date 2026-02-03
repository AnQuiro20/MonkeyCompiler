using System;
using System.IO;

namespace Monkey.Frontend
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: --emit-in-memory <source.monkey> | --emit-exe <outDir> <source.monkey> | <source.monkey>");
                return 1;
            }

            try
            {
                switch (args[0])
                {
                    case "--emit-in-memory":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Missing source file.");
                            return 2;
                        }
                        {
                            var src = File.ReadAllText(args[1]);
                            var (errors, output) = CompilerService.EmitAndRunInMemory(src);
                            if (errors.Count > 0)
                            {
                                foreach (var e in errors) Console.Error.WriteLine(e);
                                return 3;
                            }
                            Console.WriteLine(output);
                            return 0;
                        }

                    case "--emit-exe":
                        if (args.Length < 3)
                        {
                            Console.Error.WriteLine("Usage: --emit-exe <outDir> <source.monkey>");
                            return 2;
                        }
                        {
                            var outDir = args[1];
                            var src2 = File.ReadAllText(args[2]);
                            var (errs, path) = CompilerService.EmitExeToDisk(src2, outDir);
                            if (errs.Count > 0)
                            {
                                foreach (var e in errs) Console.Error.WriteLine(e);
                                return 3;
                            }
                            Console.WriteLine(path);
                            return 0;
                        }

                    case "--compile-file":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Usage: --compile-file <source.monkey>");
                            return 2;
                        }
                        {
                            var srcFile = File.ReadAllText(args[1]);
                            var (errsCf, outCf, _) = CompilerService.CompileAndRun(srcFile);
                            Console.WriteLine(outCf);
                            return errsCf.Count > 0 ? 3 : 0;
                        }

                    case "--dump-ast":
                        if (args.Length < 3)
                        {
                            Console.Error.WriteLine("Usage: --dump-ast <dotPath> <source.monkey>");
                            return 2;
                        }
                        {
                            var dotPath = args[1];
                            var srcForDot = File.ReadAllText(args[2]);
                            var (dotErrs, dotText) = CompilerService.GenerateAstDot(srcForDot);
                            if (dotErrs.Count > 0)
                            {
                                foreach (var e in dotErrs) Console.Error.WriteLine(e);
                                return 3;
                            }
                            File.WriteAllText(dotPath, dotText);
                            return 0;
                        }

                    default:
                        {
                            // Treat the first arg as a source file to compile+run via IR/VM
                            var srcDefault = File.ReadAllText(args[0]);
                            var (errs2, output2, _) = CompilerService.CompileAndRun(srcDefault);
                            // Always print the compiler output (it contains sections for errors/output/ir)
                            Console.WriteLine(output2);
                            return errs2.Count > 0 ? 3 : 0;
                        }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unhandled error: " + ex.Message);
                return 99;
            }
        }
    }
}

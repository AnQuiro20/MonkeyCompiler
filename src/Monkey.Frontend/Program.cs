using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Monkey.AST;
using Monkey.AST.Expressions;
using Monkey.AST.Statements;
using Monkey.AST.Types;
using Monkey.Common;

namespace Monkey.CodeGeneration
{
    /// Genera CIL (Reflection.Emit) recorriendo el AST:
    /// - En .NET 6/7/8: ejecuta en memoria (AssemblyBuilderAccess.Run).
    /// - En .NET Framework 4.x: opcionalmente guarda .exe (RunAndSave + Save()).
    public class ILCodeGeneratorVisitor
    {
        private AssemblyBuilder _asm = default!;
        private ModuleBuilder _mod = default!;
        private TypeBuilder _type = default!;
        private readonly Dictionary<string, MethodBuilder> _functions = new();

        private ILGenerator _il = default!;
        private MethodBuilder _currentMethod = default!;
        private readonly Stack<Dictionary<string, LocalBuilder>> _scopes = new();
        private readonly Dictionary<string, int> _paramIndex = new(); // nombre -> índice

        private static Type MapType(IType? t) =>
            t switch
            {
                IntType    => typeof(long),
                BoolType   => typeof(long), // 0/1 en la pila
                StringType => typeof(string),
                VoidType   => typeof(void),
                _          => typeof(long)
            };

        // ============ API pública ============

        public void EmitAndRun(IASTNode root)
        {
            BuildAssembly(root, saveExe: false, exePath: null);
            RunMain();
        }

        public void EmitAndSaveExe(IASTNode root, string exePath)
        {
#if NETFRAMEWORK
            BuildAssembly(root, saveExe: true, exePath: exePath);
            Console.WriteLine($"💾 EXE generado: {exePath}");
#else
            throw new NotSupportedException(
                "Guardar .exe con Reflection.Emit solo está soportado en .NET Framework 4.x. " +
                "En .NET 6/7/8 no existe AssemblyBuilder.Save().");
#endif
        }

        // ============ Construcción del ensamblado ============

        private void BuildAssembly(IASTNode root, bool saveExe, string? exePath)
        {
            var an = new AssemblyName("MonkeyDynamicAssembly");

#if NETFRAMEWORK
            _asm = AppDomain.CurrentDomain.DefineDynamicAssembly(
                an,
                saveExe ? AssemblyBuilderAccess.RunAndSave : AssemblyBuilderAccess.Run
            );
            _mod = saveExe
                ? _asm.DefineDynamicModule("MonkeyModule", System.IO.Path.GetFileName(exePath))
                : _asm.DefineDynamicModule("MonkeyModule");
#else
            if (saveExe) throw new NotSupportedException("Guardar .exe requiere .NET Framework 4.x");
            _asm = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            _mod = _asm.DefineDynamicModule("MonkeyModule");
#endif
            _type = _mod.DefineType("MonkeyProgram", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

            // Primera pasada: declarar firmas de funciones (incluye main)
            if (root is Monkey.AST.Program prog)
            {
                foreach (var d in prog.Declarations.OfType<FunctionDeclaration>())
                    DeclareFunctionSignature(d);

                if (prog.MainFunction is FunctionDeclaration mainDecl)
                    DeclareFunctionSignature(mainDecl);
            }

            // Segunda pasada: emitir cuerpos
            VisitNode(root);

            var programType = _type.CreateType();

#if NETFRAMEWORK
            if (saveExe && exePath != null)
            {
                // Crear EntryPoint: un Main() que llame a main()
                var eb = programType!.DefineMethod(
                    "Main",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    Type.EmptyTypes
                );
                var il = eb.GetILGenerator();
                var hasMain = _functions.TryGetValue("main", out var mainMb);

                if (hasMain)
                    il.Emit(OpCodes.Call, mainMb!);
                il.Emit(OpCodes.Ret);

                _asm.SetEntryPoint(eb, PEFileKinds.ConsoleApplication);
                _asm.Save(System.IO.Path.GetFileName(exePath));
            }
#endif
        }

        private void DeclareFunctionSignature(FunctionDeclaration f)
        {
            if (_functions.ContainsKey(f.Name.Value)) return;

            var rt = MapType(f.ReturnType ?? new VoidType());
            var pTypes = f.Parameters.Select(p => MapType(p.Type)).ToArray();
            var mb = _type.DefineMethod(
                f.Name.Value,
                MethodAttributes.Public | MethodAttributes.Static,
                rt,
                pTypes
            );
            for (int i = 0; i < f.Parameters.Count; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None, f.Parameters[i].Name);

            _functions[f.Name.Value] = mb;
        }

        private void RunMain()
        {
            Console.WriteLine("\n▶️ Ejecutando (Reflection.Emit)...");
            var t = _asm.DefinedTypes.First(x => x.Name == "MonkeyProgram").AsType();
            var main = t.GetMethod("main", BindingFlags.Public | BindingFlags.Static);
            if (main == null)
            {
                Console.WriteLine("⚠️ No se encontró función 'main'.");
                return;
            }
            main.Invoke(null, Array.Empty<object>());
            Console.WriteLine("✅ Ejecución (IL) completada.");
        }

        // ============ Visitor ============

        public void VisitNode(IASTNode? node)
        {
            if (node == null) return;

            switch (node)
            {
                case Monkey.AST.Program p:
                    foreach (var d in p.Declarations.OfType<FunctionDeclaration>())
                        EmitFunction(d);
                    if (p.MainFunction is FunctionDeclaration main)
                        EmitFunction(main);
                    break;

                case FunctionDeclaration f:
                    EmitFunction(f);
                    break;

                case BlockStatement b:
                    EnterScope();
                    foreach (var s in b.Statements) VisitNode(s);
                    LeaveScope();
                    break;

                case LetStatement let:
                    EmitLet(let);
                    break;

                case ExpressionStatement es:
                    EmitExpr(es.Expression);
                    _il.Emit(OpCodes.Pop); // descartamos resultado
                    break;

                case ReturnStatement rs:
                    EmitReturn(rs);
                    break;

                case IfStatement ifs:
                    EmitIf(ifs);
                    break;

                case WhileStatement wh:
                    EmitWhile(wh);
                    break;

                case PrintStatement ps:
                    EmitPrint(ps.Expression);
                    break;

                default:
                    EmitExpr(node as IExpression);
                    break;
            }
        }

        private void EmitFunction(FunctionDeclaration f)
        {
            _currentMethod = _functions[f.Name.Value];
            _il = _currentMethod.GetILGenerator();
            _paramIndex.Clear();

            for (int i = 0; i < f.Parameters.Count; i++)
                _paramIndex[f.Parameters[i].Name] = i;

            EnterScope();
            VisitNode(f.Body);
            if (MapType(f.ReturnType ?? new VoidType()) == typeof(void))
                _il.Emit(OpCodes.Ret);
            LeaveScope();
        }

        private void EmitLet(LetStatement let)
        {
            var local = _il.DeclareLocal(MapType(let.Type ?? new IntType()));
            CurrentScope()[let.Identifier.Value] = local;
            EmitExpr(let.Value);
            _il.Emit(OpCodes.Stloc, local);
        }

        private void EmitReturn(ReturnStatement r)
        {
            if (r.Expression != null) EmitExpr(r.Expression);
            _il.Emit(OpCodes.Ret);
        }


        private void EmitIf(IfStatement ifs)
        {
            var lblElse = _il.DefineLabel();
            var lblEnd  = _il.DefineLabel();

            EmitExpr(ifs.Condition);
            _il.Emit(OpCodes.Ldc_I8, 0L);
            _il.Emit(OpCodes.Ceq); // 1 si cond == 0
            _il.Emit(OpCodes.Brtrue, lblElse);

            VisitNode(ifs.Consequence);
            _il.Emit(OpCodes.Br, lblEnd);

            _il.MarkLabel(lblElse);
            if (ifs.Alternative != null)
                VisitNode(ifs.Alternative);

            _il.MarkLabel(lblEnd);
        }

        private void EmitWhile(WhileStatement wh)
        {
            var lblStart = _il.DefineLabel();
            var lblEnd   = _il.DefineLabel();

            _il.MarkLabel(lblStart);
            EmitExpr(wh.Condition);
            _il.Emit(OpCodes.Ldc_I8, 0L);
            _il.Emit(OpCodes.Ceq);
            _il.Emit(OpCodes.Brtrue, lblEnd);

            VisitNode(wh.Body);
            _il.Emit(OpCodes.Br, lblStart);
            _il.MarkLabel(lblEnd);
        }

        private void EmitPrint(IExpression expr)
        {
            EmitExpr(expr);
            var t = InferClrType(expr) ?? typeof(long);
            if (t == typeof(string))
            {
                var m = typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(string) })!;
                _il.Emit(OpCodes.Call, m);
            }
            else
            {
                _il.Emit(OpCodes.Conv_I8);
                var m = typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(long) })!;
                _il.Emit(OpCodes.Call, m);
            }
        }

        // ============ Expresiones ============

        private void EmitExpr(IExpression? expr)
        {
            if (expr == null) return;

            switch (expr)
            {
                case IntegerLiteral i:
                    _il.Emit(OpCodes.Ldc_I8, (long)i.Value);
                    break;

                case BooleanLiteral b:
                    _il.Emit(OpCodes.Ldc_I8, b.Value ? 1L : 0L);
                    break;

                case StringLiteral s:
                    _il.Emit(OpCodes.Ldstr, s.Value ?? string.Empty);
                    break;

                case Identifier id:
                    if (TryLoadLocal(id.Value)) return;
                    if (TryLoadParameter(id.Value)) return;
                    throw new Exception($"Identificador no resuelto: {id.Value}");

                case BinaryExpression bin:
                    EmitExpr(bin.Left);
                    EmitExpr(bin.Right);
                    EmitBinary(bin.Operator);
                    break;

                case CallExpression call:
                    EmitCall(call);
                    break;

                default:
                    throw new NotSupportedException($"Expresión no soportada: {expr.GetType().Name}");
            }
        }

        private void EmitBinary(BinaryOperator op)
        {
            switch (op)
            {
                case BinaryOperator.Add:      _il.Emit(OpCodes.Add); break;
                case BinaryOperator.Subtract: _il.Emit(OpCodes.Sub); break;
                case BinaryOperator.Multiply: _il.Emit(OpCodes.Mul); break;
                case BinaryOperator.Divide:   _il.Emit(OpCodes.Div); break;

                case BinaryOperator.Equal:
                    _il.Emit(OpCodes.Ceq);
                    _il.Emit(OpCodes.Conv_I8);
                    break;

                case BinaryOperator.Less:
                    _il.Emit(OpCodes.Clt);
                    _il.Emit(OpCodes.Conv_I8);
                    break;

                case BinaryOperator.Greater:
                    _il.Emit(OpCodes.Cgt);
                    _il.Emit(OpCodes.Conv_I8);
                    break;

                case BinaryOperator.LessOrEqual:
                    _il.Emit(OpCodes.Cgt);         // 1 si a > b
                    _il.Emit(OpCodes.Ldc_I4_0);
                    _il.Emit(OpCodes.Ceq);         // 1 si !(a > b)
                    _il.Emit(OpCodes.Conv_I8);
                    break;

                case BinaryOperator.GreaterOrEqual:
                    _il.Emit(OpCodes.Clt);         // 1 si a < b
                    _il.Emit(OpCodes.Ldc_I4_0);
                    _il.Emit(OpCodes.Ceq);         // 1 si !(a < b)
                    _il.Emit(OpCodes.Conv_I8);
                    break;

                default:
                    throw new NotSupportedException($"Operador no soportado: {op}");
            }
        }

        private void EmitCall(CallExpression c)
        {
            if (c.Callee is not Identifier id)
                throw new Exception("Llamada sin identificador.");

            if (!_functions.TryGetValue(id.Value, out var mb))
                throw new Exception($"Función no declarada: {id.Value}");

            foreach (var a in c.Arguments) EmitExpr(a);
            _il.Emit(OpCodes.Call, mb);
        }

        // ============ Scopes y parámetros ============

        private void EnterScope() => _scopes.Push(new Dictionary<string, LocalBuilder>());
        private void LeaveScope() => _scopes.Pop();
        private Dictionary<string, LocalBuilder> CurrentScope() => _scopes.Peek();

        private bool TryLoadLocal(string name)
        {
            foreach (var scope in _scopes)
                if (scope.TryGetValue(name, out var loc))
                { _il.Emit(OpCodes.Ldloc, loc); return true; }
            return false;
        }

        private bool TryLoadParameter(string name)
        {
            if (!_paramIndex.TryGetValue(name, out var idx)) return false;
            switch (idx)
            {
                case 0: _il.Emit(OpCodes.Ldarg_0); break;
                case 1: _il.Emit(OpCodes.Ldarg_1); break;
                case 2: _il.Emit(OpCodes.Ldarg_2); break;
                case 3: _il.Emit(OpCodes.Ldarg_3); break;
                default: _il.Emit(OpCodes.Ldarg_S, (byte)idx); break;
            }
            return true;
        }

        private static Type? InferClrType(IExpression expr) =>
            expr switch
            {
                IntegerLiteral => typeof(long),
                BooleanLiteral => typeof(long),
                StringLiteral  => typeof(string),
                _              => null
            };
    }
}

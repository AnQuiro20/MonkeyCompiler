using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.IO;
using Monkey.AST;
using Monkey.Common;
using Monkey.AST.Statements;
using Monkey.AST.Expressions;
using Monkey.AST.Types;

namespace Monkey.CodeGeneration
{
    // Visitor that emits a dynamic assembly (in-memory) containing a static method per function.
    // Each generated method has signature: public static object func(object[] args)
    // This implementation aims to cover the core Monkey language (literals, vars, arithmetic,
    // prints, function calls, returns, if/while, blocks). It's a pragmatic but functional emitter.
    public class CilCodeGeneratorVisitor
    {
        private AssemblyBuilder _asmBuilder = null!;
        private ModuleBuilder _moduleBuilder = null!;
        private TypeBuilder _typeBuilder = null!;

        // mapping function name -> MethodBuilder
        private readonly Dictionary<string, MethodBuilder> _methodBuilders = new();

        // mapping function name -> FunctionDeclaration AST
        private readonly Dictionary<string, FunctionDeclaration> _functions = new();

        public string EmitAndRun(Monkey.AST.Program program)
        {
            var asmName = new AssemblyName("Monkey.Dynamic");
            _asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            _moduleBuilder = _asmBuilder.DefineDynamicModule("MainModule");
            _typeBuilder = _moduleBuilder.DefineType("MonkeyProgram", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

            // 1) Collect functions
            foreach (var decl in program.Declarations)
            {
                if (decl is FunctionDeclaration fd)
                    _functions[fd.Name.Value] = fd;
            }
            if (program.MainFunction is FunctionDeclaration mainFd)
                _functions[mainFd.Name.Value] = mainFd;

            // 2) Define method builders for each function
            foreach (var kv in _functions)
            {
                var name = kv.Key;
                var mb = _typeBuilder.DefineMethod($"func_{name}", MethodAttributes.Public | MethodAttributes.Static, typeof(object), new Type[] { typeof(object[]) });
                _methodBuilders[name] = mb;
            }

            // 3) Emit IL for each function
            foreach (var kv in _functions)
            {
                var name = kv.Key;
                var func = kv.Value;
                var mb = _methodBuilders[name];
                var il = mb.GetILGenerator();

                EmitFunctionBody(il, func);
            }

            // 4) Create type
            var created = _typeBuilder.CreateType()!;

            // 5) Execute main and capture output
            var mainName = "main";
            if (!_methodBuilders.ContainsKey(mainName))
                throw new InvalidOperationException("No se encontró función 'main' para ejecutar.");

            var methodInfo = created.GetMethod($"func_{mainName}", BindingFlags.Public | BindingFlags.Static)!;

            var sw = new StringWriter();
            var oldOut = Console.Out;
            try
            {
                Console.SetOut(sw);
                methodInfo.Invoke(null, new object[] { new object[0] });
            }
            finally
            {
                Console.SetOut(oldOut);
            }

            return sw.ToString();
        }

        private void EmitFunctionBody(ILGenerator il, FunctionDeclaration func)
        {
            // Prepare locals: one local per parameter + locals discovered, all typed as object
            var paramNames = func.Parameters.Select(p => p.Name).ToList();

            var localMap = new Dictionary<string, LocalBuilder>();
            var localTypes = new Dictionary<string, IType>();

            // declare locals for parameters
            for (int i = 0; i < paramNames.Count; i++)
            {
                var lb = il.DeclareLocal(typeof(object));
                localMap[paramNames[i]] = lb;
                localTypes[paramNames[i]] = func.Parameters[i].Type;
            }

            // collect let-declared variables in body
            var letVars = CollectLetVariables(func.Body);
            foreach (var v in letVars)
            {
                if (localMap.ContainsKey(v)) continue;
                var lb = il.DeclareLocal(typeof(object));
                localMap[v] = lb;
                // default unknown type
                localTypes[v] = new Monkey.AST.Types.VoidType();
            }

            // temps for expression evaluation
            var tmp1 = il.DeclareLocal(typeof(object));
            var tmp2 = il.DeclareLocal(typeof(object));

            // initialize parameters from args[] (arg0)
            for (int i = 0; i < paramNames.Count; i++)
            {
                // ldarg.0, ldc.i4 i, ldelem.ref, stloc localMap[param]
                il.Emit(OpCodes.Ldarg_0);
                EmitLdcI4(il, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Stloc, localMap[paramNames[i]]);
            }

            // Emit statements
            EmitBlockStatements(il, func.Body, localMap, localTypes, tmp1, tmp2);

            // If no explicit return, return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        private List<string> CollectLetVariables(BlockStatement block)
        {
            var res = new List<string>();
            foreach (var stmt in block.Statements)
            {
                if (stmt is LetStatement ls)
                {
                    res.Add(ls.Identifier.Value);
                }
                else if (stmt is BlockStatement bs)
                {
                    res.AddRange(CollectLetVariables(bs));
                }
                else if (stmt is IfStatement ifs)
                {
                    res.AddRange(CollectLetVariables(ifs.Consequence));
                    if (ifs.Alternative != null) res.AddRange(CollectLetVariables(ifs.Alternative));
                }
            }
            return res.Distinct().ToList();
        }

        private void EmitBlockStatements(ILGenerator il, BlockStatement block, Dictionary<string, LocalBuilder> localMap, Dictionary<string, IType> localTypes, LocalBuilder tmp1, LocalBuilder tmp2)
        {
            foreach (var s in block.Statements)
            {
                EmitStatement(il, s, localMap, localTypes, tmp1, tmp2);
            }
        }

    private void EmitMethodForFunctionLiteral(string name, FunctionLiteral fl)
        {
            // define method with signature object func_name(object[] args)
            if (_methodBuilders.ContainsKey(name)) return; // already defined
            var mb = _typeBuilder.DefineMethod($"func_{name}", MethodAttributes.Public | MethodAttributes.Static, typeof(object), new Type[] { typeof(object[]) });
            _methodBuilders[name] = mb;
            var il = mb.GetILGenerator();

            // parameters
            var paramNames = fl.Parameters.Select(p => p.Name).ToList();
            var localMap = new Dictionary<string, LocalBuilder>();
            var localTypes = new Dictionary<string, IType>();

            for (int i = 0; i < paramNames.Count; i++)
            {
                var lb = il.DeclareLocal(typeof(object));
                localMap[paramNames[i]] = lb;
                localTypes[paramNames[i]] = fl.Parameters[i].Type;
            }

            // collect lets
            var letVars = CollectLetVariables(fl.Body);
            foreach (var v in letVars)
            {
                if (localMap.ContainsKey(v)) continue;
                var lb = il.DeclareLocal(typeof(object));
                localMap[v] = lb;
                localTypes[v] = new VoidType();
            }

            var tmp1 = il.DeclareLocal(typeof(object));
            var tmp2 = il.DeclareLocal(typeof(object));

            // load args into params
            for (int i = 0; i < paramNames.Count; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                EmitLdcI4(il, i);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Stloc, localMap[paramNames[i]]);
            }

            EmitBlockStatements(il, fl.Body, localMap, localTypes, tmp1, tmp2);

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        private void EmitStatement(ILGenerator il, IASTNode stmtNode, Dictionary<string, LocalBuilder> localMap, Dictionary<string, IType> localTypes, LocalBuilder tmp1, LocalBuilder tmp2)
        {
            switch (stmtNode)
            {
                case LetStatement let:
                    // If this let assigns a function literal, define a method for it with the variable name
                    if (let.Value is FunctionLiteral fl)
                    {
                        EmitMethodForFunctionLiteral(let.Identifier.Value, fl);
                        // store null into the variable (we resolve calls by name via _methodBuilders)
                        if (!localMap.TryGetValue(let.Identifier.Value, out var lb2)) lb2 = il.DeclareLocal(typeof(object));
                        il.Emit(OpCodes.Ldnull);
                        il.Emit(OpCodes.Stloc, lb2);
                        localTypes[let.Identifier.Value] = let.Type ?? new Monkey.AST.Types.FunctionType(new Monkey.AST.Types.VoidType());
                        break;
                    }

                    EmitExpression(il, let.Value, localMap, localTypes, tmp1, tmp2);
                    // value on stack (object), store to local
                    if (!localMap.TryGetValue(let.Identifier.Value, out var lb))
                        lb = il.DeclareLocal(typeof(object));
                    il.Emit(OpCodes.Stloc, lb);
                    localTypes[let.Identifier.Value] = let.Type ?? new Monkey.AST.Types.VoidType();
                    break;

                case ExpressionStatement es:
                    EmitExpression(il, es.Expression, localMap, localTypes, tmp1, tmp2);
                    // pop result
                    il.Emit(OpCodes.Pop);
                    break;

                case PrintStatement ps:
                    EmitExpression(il, ps.Expression, localMap, localTypes, tmp1, tmp2);
                    // call Console.WriteLine(object)
                    var writeLine = typeof(Console).GetMethod("WriteLine", new Type[] { typeof(object) })!;
                    il.Emit(OpCodes.Call, writeLine);
                    break;

                case ReturnStatement rs:
                    EmitExpression(il, rs.Expression, localMap, localTypes, tmp1, tmp2);
                    il.Emit(OpCodes.Ret);
                    break;

                case BlockStatement bs:
                    EmitBlockStatements(il, bs, localMap, localTypes, tmp1, tmp2);
                    break;

                case IfStatement ifs:
                    {
                        var elseLabel = il.DefineLabel();
                        var endLabel = il.DefineLabel();
                        EmitExpression(il, ifs.Condition, localMap, localTypes, tmp1, tmp2);
                        // top is object, unbox to bool
                        il.Emit(OpCodes.Unbox_Any, typeof(bool));
                        il.Emit(OpCodes.Brfalse, elseLabel);
                        EmitBlockStatements(il, ifs.Consequence, localMap, localTypes, tmp1, tmp2);
                        il.Emit(OpCodes.Br, endLabel);
                        il.MarkLabel(elseLabel);
                        if (ifs.Alternative != null) EmitBlockStatements(il, ifs.Alternative, localMap, localTypes, tmp1, tmp2);
                        il.MarkLabel(endLabel);
                        break;
                    }

                case WhileStatement ws:
                    {
                        var start = il.DefineLabel();
                        var end = il.DefineLabel();
                        il.MarkLabel(start);
                        EmitExpression(il, ws.Condition, localMap, localTypes, tmp1, tmp2);
                        il.Emit(OpCodes.Unbox_Any, typeof(bool));
                        il.Emit(OpCodes.Brfalse, end);
                        EmitBlockStatements(il, ws.Body, localMap, localTypes, tmp1, tmp2);
                        il.Emit(OpCodes.Br, start);
                        il.MarkLabel(end);
                        break;
                    }

                case FunctionDeclaration _:
                    // function declarations are handled at top-level
                    break;

                default:
                    // Unknown statement - ignore
                    break;
            }
        }

        private void EmitExpression(ILGenerator il, IASTNode exprNode, Dictionary<string, LocalBuilder> localMap, Dictionary<string, IType> localTypes, LocalBuilder tmp1, LocalBuilder tmp2)
        {
            switch (exprNode)
            {
                case IntegerLiteral ilit:
                    il.Emit(OpCodes.Ldc_I8, ilit.Value);
                    il.Emit(OpCodes.Box, typeof(long));
                    break;

                case StringLiteral slit:
                    il.Emit(OpCodes.Ldstr, slit.Value);
                    break;

                case BooleanLiteral blit:
                    il.Emit(blit.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Box, typeof(bool));
                    break;

                case Identifier id:
                    if (!localMap.TryGetValue(id.Value, out var lb))
                        throw new InvalidOperationException($"Variable no declarada: {id.Value}");
                    il.Emit(OpCodes.Ldloc, lb);
                    break;

                case BinaryExpression bin:
                    // evaluate left and right into tmp1/tmp2
                    EmitExpression(il, bin.Left as IASTNode ?? throw new InvalidOperationException("Left null"), localMap, localTypes, tmp1, tmp2);
                    il.Emit(OpCodes.Stloc, tmp1);
                    EmitExpression(il, bin.Right as IASTNode ?? throw new InvalidOperationException("Right null"), localMap, localTypes, tmp1, tmp2);
                    il.Emit(OpCodes.Stloc, tmp2);

                    // load tmp1 (left), unbox long
                    il.Emit(OpCodes.Ldloc, tmp1);
                    il.Emit(OpCodes.Unbox_Any, typeof(long));
                    // load tmp2 (right)
                    il.Emit(OpCodes.Ldloc, tmp2);
                    il.Emit(OpCodes.Unbox_Any, typeof(long));

                    switch (bin.Operator)
                    {
                        case BinaryOperator.Add: il.Emit(OpCodes.Add); il.Emit(OpCodes.Box, typeof(long)); break;
                        case BinaryOperator.Subtract: il.Emit(OpCodes.Sub); il.Emit(OpCodes.Box, typeof(long)); break;
                        case BinaryOperator.Multiply: il.Emit(OpCodes.Mul); il.Emit(OpCodes.Box, typeof(long)); break;
                        case BinaryOperator.Divide: il.Emit(OpCodes.Div); il.Emit(OpCodes.Box, typeof(long)); break;
                        case BinaryOperator.Less:
                            il.Emit(OpCodes.Clt);
                            il.Emit(OpCodes.Box, typeof(bool));
                            break;
                        case BinaryOperator.Greater:
                            il.Emit(OpCodes.Cgt);
                            il.Emit(OpCodes.Box, typeof(bool));
                            break;
                        case BinaryOperator.LessOrEqual:
                            il.Emit(OpCodes.Cgt);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Box, typeof(bool));
                            break;
                        case BinaryOperator.GreaterOrEqual:
                            il.Emit(OpCodes.Clt);
                            il.Emit(OpCodes.Ldc_I4_0);
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Box, typeof(bool));
                            break;
                        case BinaryOperator.Equal:
                            il.Emit(OpCodes.Ceq);
                            il.Emit(OpCodes.Box, typeof(bool));
                            break;
                    }
                    // (boxing performed per-case above)
                    break;

                case CallExpression call:
                    // create object[] args
                    var argc = call.Arguments.Count;
                    // build args array in a local
                    var argsLocal = il.DeclareLocal(typeof(object[]));
                    EmitLdcI4(il, argc);
                    il.Emit(OpCodes.Newarr, typeof(object));
                    il.Emit(OpCodes.Stloc, argsLocal);
                    for (int i = 0; i < argc; i++)
                    {
                        il.Emit(OpCodes.Ldloc, argsLocal); // array
                        EmitLdcI4(il, i); // index
                        EmitExpression(il, call.Arguments[i] as IASTNode ?? throw new InvalidOperationException("arg null"), localMap, localTypes, tmp1, tmp2); // value
                        il.Emit(OpCodes.Stelem_Ref);
                    }

                    // call the method: load args array
                    il.Emit(OpCodes.Ldloc, argsLocal);
                    if (!_methodBuilders.TryGetValue(call.FunctionName, out var targetMb))
                        throw new InvalidOperationException($"Función no encontrada: {call.FunctionName}");
                    il.Emit(OpCodes.Call, targetMb);
                    break;

                case ArrayLiteral arr:
                    {
                        var len = arr.Elements.Count;
                        var arrLocal = il.DeclareLocal(typeof(object[]));
                        EmitLdcI4(il, len);
                        il.Emit(OpCodes.Newarr, typeof(object));
                        il.Emit(OpCodes.Stloc, arrLocal);
                        for (int i = 0; i < len; i++)
                        {
                            il.Emit(OpCodes.Ldloc, arrLocal);
                            EmitLdcI4(il, i);
                            EmitExpression(il, arr.Elements[i] as IASTNode ?? throw new InvalidOperationException("elem null"), localMap, localTypes, tmp1, tmp2);
                            il.Emit(OpCodes.Stelem_Ref);
                        }
                        il.Emit(OpCodes.Ldloc, arrLocal);
                        break;
                    }

                case HashLiteral h:
                    {
                        var dictType = typeof(Dictionary<object, object>);
                        var dictLocal = il.DeclareLocal(dictType);
                        var ctor = dictType.GetConstructor(Type.EmptyTypes)!;
                        il.Emit(OpCodes.Newobj, ctor);
                        il.Emit(OpCodes.Stloc, dictLocal);
                        var addMethod = dictType.GetMethod("Add", new Type[] { typeof(object), typeof(object) })!;
                        foreach (var kv in h.Pairs)
                        {
                            il.Emit(OpCodes.Ldloc, dictLocal);
                            EmitExpression(il, kv.Key as IASTNode ?? throw new InvalidOperationException("hash key null"), localMap, localTypes, tmp1, tmp2);
                            EmitExpression(il, kv.Value as IASTNode ?? throw new InvalidOperationException("hash value null"), localMap, localTypes, tmp1, tmp2);
                            il.Emit(OpCodes.Callvirt, addMethod);
                        }
                        il.Emit(OpCodes.Ldloc, dictLocal);
                        break;
                    }

                case FunctionLiteral fl:
                    {
                        // define a generated method for this literal (unnamed) - generate unique name
                        var genName = "lit_" + Guid.NewGuid().ToString("N");
                        EmitMethodForFunctionLiteral(genName, fl);
                        // push null as placeholder (we don't create closures/delegates in this PoC)
                        il.Emit(OpCodes.Ldnull);
                        break;
                    }

                case IndexExpression idxExp:
                    {
                        // If left is an identifier we can check its declared type
                        if (idxExp.Left is Identifier leftId && localMap.TryGetValue(leftId.Value, out var leftLb) && localTypes.TryGetValue(leftId.Value, out var leftType))
                        {
                            if (leftType is Monkey.AST.Types.ArrayType)
                            {
                                // array access: assume object[]
                                il.Emit(OpCodes.Ldloc, leftLb);
                                // index
                                if (idxExp.Index is IntegerLiteral icol)
                                {
                                    EmitLdcI4(il, (int)icol.Value);
                                }
                                else
                                {
                                    EmitExpression(il, idxExp.Index as IASTNode ?? throw new InvalidOperationException("index null"), localMap, localTypes, tmp1, tmp2);
                                    // index is boxed long -> unbox and conv.i4
                                    il.Emit(OpCodes.Unbox_Any, typeof(long));
                                    il.Emit(OpCodes.Conv_I4);
                                }
                                il.Emit(OpCodes.Ldelem_Ref);
                                break;
                            }
                            else if (leftType is Monkey.AST.Types.HashType)
                            {
                                // dictionary access: Dictionary<object,object>
                                var dictType = typeof(Dictionary<object, object>);
                                var dictTemp = il.DeclareLocal(dictType);
                                il.Emit(OpCodes.Ldloc, leftLb);
                                il.Emit(OpCodes.Castclass, dictType);
                                il.Emit(OpCodes.Stloc, dictTemp);
                                il.Emit(OpCodes.Ldloc, dictTemp);
                                EmitExpression(il, idxExp.Index as IASTNode ?? throw new InvalidOperationException("hash index null"), localMap, localTypes, tmp1, tmp2);
                                var getter = dictType.GetProperty("Item")!.GetGetMethod()!;
                                il.Emit(OpCodes.Callvirt, getter);
                                break;
                            }
                        }
                        // Fallback: evaluate left and index and try array access
                        EmitExpression(il, idxExp.Left as IASTNode ?? throw new InvalidOperationException("left null"), localMap, localTypes, tmp1, tmp2);
                        il.Emit(OpCodes.Castclass, typeof(object[]));
                        if (idxExp.Index is IntegerLiteral ilit2)
                        {
                            EmitLdcI4(il, (int)ilit2.Value);
                        }
                        else
                        {
                            EmitExpression(il, idxExp.Index as IASTNode ?? throw new InvalidOperationException("index null"), localMap, localTypes, tmp1, tmp2);
                            il.Emit(OpCodes.Unbox_Any, typeof(long));
                            il.Emit(OpCodes.Conv_I4);
                        }
                        il.Emit(OpCodes.Ldelem_Ref);
                        break;
                    }

                default:
                    throw new NotImplementedException($"Expresión no soportada por el emisor CIL: {exprNode.GetType().Name}");
            }
        }

        private void EmitLdcI4(ILGenerator il, int v)
        {
            switch (v)
            {
                case -1: il.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                case 2: il.Emit(OpCodes.Ldc_I4_2); break;
                case 3: il.Emit(OpCodes.Ldc_I4_3); break;
                case 4: il.Emit(OpCodes.Ldc_I4_4); break;
                case 5: il.Emit(OpCodes.Ldc_I4_5); break;
                default:
                    if (v >= -128 && v <= 127) il.Emit(OpCodes.Ldc_I4_S, (sbyte)v);
                    else il.Emit(OpCodes.Ldc_I4, v);
                    break;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Monkey.AST;
using Monkey.AST.Expressions;
using Monkey.AST.Statements;
using Monkey.AST.Types;
using Monkey.Common;
using Monkey.SymbolTable;

namespace Monkey.TypeChecking
{
    public class TypeCheckerVisitor
    {
        private readonly SymbolTableBuilderVisitor _symbols;
        private readonly List<string> _errors = new();

        // Scopes locales (para parámetros y variables dentro de funciones/bloques)
        private readonly Stack<Dictionary<string, IType>> _localScopes = new();

        // Pila de tipo de retorno de la función actual
        private readonly Stack<IType> _returnTypeStack = new();

        public TypeCheckerVisitor(SymbolTableBuilderVisitor symbols)
        {
            _symbols = symbols;
        }

        public IReadOnlyList<string> GetErrors() => _errors.AsReadOnly();

        public void PrintErrors()
        {
            if (_errors.Count == 0)
            {
                Console.WriteLine("✅ Análisis de tipos completado sin errores.");
                return;
            }

            Console.WriteLine("\n❌ Errores de tipos encontrados:");
            foreach (var e in _errors)
                Console.WriteLine("❌ " + e);
        }

        // ============================================================
        // VISITADOR PRINCIPAL
        // ============================================================
        public void VisitNode(IASTNode? node)
        {
            if (node == null) return;

            switch (node)
            {
                case Monkey.AST.Program prog:
                    foreach (var d in prog.Declarations)
                        VisitNode(d);

                    VisitNode(prog.MainFunction);

                    foreach (var s in prog.Statements)
                        VisitNode(s);
                    break;

                case FunctionDeclaration func:
                    VisitFunction(func);
                    break;

                case BlockStatement block:
                    VisitBlock(block);
                    break;

                case LetStatement let:
                    VisitLet(let);
                    break;

                case ReturnStatement ret:
                    VisitReturn(ret);
                    break;

                case ExpressionStatement es:
                    GetExpressionType(es.Expression); // solo validamos que sea correcto
                    break;

                case IfStatement ifs:
                    VisitIf(ifs);
                    break;

                case WhileStatement wh:
                    VisitWhile(wh);
                    break;

                case PrintStatement ps:
                    GetExpressionType(ps.Expression);
                    break;
            }
        }

        // ============================================================
        // FUNCIÓN
        // ============================================================
        private void VisitFunction(FunctionDeclaration func)
        {
            // Scope para parámetros + variables dentro de la función
            _localScopes.Push(new Dictionary<string, IType>());

            // Registrar parámetros en el scope local
            foreach (var p in func.Parameters)
                _localScopes.Peek()[p.Name] = p.Type;

            _returnTypeStack.Push(func.ReturnType);

            // Validar cuerpo
            VisitNode(func.Body);

            // Funciones NO-void deben tener al menos un return en todos los caminos
            if (!(func.ReturnType is VoidType))
            {
                if (!ContainsReturn(func.Body))
                {
                    _errors.Add(
                        $"Error de tipo: la función '{func.Name.Value}' declara retorno {func.ReturnType.GetType().Name} pero no devuelve ningún valor."
                    );
                }
            }

            _returnTypeStack.Pop();
            _localScopes.Pop();
        }

        // ============================================================
        // BLOQUES Y LET
        // ============================================================
        private void VisitBlock(BlockStatement block)
        {
            // Nuevo scope para el bloque
            _localScopes.Push(new Dictionary<string, IType>());

            foreach (var stmt in block.Statements)
                VisitNode(stmt);

            _localScopes.Pop();
        }

        private void VisitLet(LetStatement let)
        {
            var exprType = GetExpressionType(let.Value);

            if (!TypeEquals(exprType, let.Type))
            {
                _errors.Add($"Error de tipo: '{let.Identifier.Value}' esperaba {let.Type.GetType().Name}.");
            }

            // Registrar la variable solo si hay scope local (dentro de función/bloque)
            if (_localScopes.Count > 0)
                _localScopes.Peek()[let.Identifier.Value] = let.Type;
            // A nivel global, ya la registró SymbolTableBuilderVisitor en _symbols.Symbols
        }

        private void VisitReturn(ReturnStatement ret)
        {
            if (_returnTypeStack.Count == 0)
            {
                _errors.Add("Error: 'return' fuera de una función.");
                return;
            }

            var expected = _returnTypeStack.Peek();
            var actual = GetExpressionType(ret.Expression);

            if (!TypeEquals(expected, actual))
            {
                _errors.Add($"Error de tipo: return devuelve {actual.GetType().Name} pero se esperaba {expected.GetType().Name}.");
            }
        }

        private void VisitIf(IfStatement ifs)
        {
            var cond = GetExpressionType(ifs.Condition);
            if (!TypeEquals(cond, new BoolType()))
                _errors.Add("Error de tipo: condición del if debe ser bool.");

            VisitNode(ifs.Consequence);

            if (ifs.Alternative != null)
                VisitNode(ifs.Alternative);
        }

        private void VisitWhile(WhileStatement wh)
        {
            var cond = GetExpressionType(wh.Condition);
            if (!TypeEquals(cond, new BoolType()))
                _errors.Add("Error de tipo: condición del while debe ser bool.");

            VisitNode(wh.Body);
        }

        // ============================================================
        // ANÁLISIS DE EXPRESIONES
        // ============================================================
        private IType GetExpressionType(IExpression? expr)
        {
            if (expr == null) return new UnknownType();

            switch (expr)
            {
                case IntegerLiteral:
                    return new IntType();
                case StringLiteral:
                    return new StringType();
                case BooleanLiteral:
                    return new BoolType();
                case CharLiteral:
                    return new CharType();

                // ---------- Arrays ----------
                case Monkey.AST.Expressions.ArrayLiteral arrLit:
                    {
                        if (arrLit.Elements.Count == 0)
                            return new ArrayType(new UnknownType());

                        var firstT = GetExpressionType(arrLit.Elements[0]);
                        for (int i = 1; i < arrLit.Elements.Count; i++)
                        {
                            var et = GetExpressionType(arrLit.Elements[i]);
                            if (!TypeEquals(firstT, et))
                            {
                                firstT = new UnknownType();
                                break;
                            }
                        }
                        return new ArrayType(firstT);
                    }

                // ---------- Hash ----------
                case Monkey.AST.Expressions.HashLiteral hashLit:
                    {
                        if (hashLit.Pairs.Count == 0)
                            return new HashType(new UnknownType(), new UnknownType());

                        var seenLiteralKeys = new HashSet<string>();
                        IType? inferredKeyType = null;
                        IType? inferredValueType = null;

                        foreach (var kv in hashLit.Pairs)
                        {
                            var keyExpr = kv.Key;
                            var valExpr = kv.Value;

                            string? normKey = null;
                            if (keyExpr is StringLiteral sk)
                                normKey = "s:" + sk.Value;
                            else if (keyExpr is IntegerLiteral ik)
                                normKey = "i:" + ik.Value.ToString();
                            else if (keyExpr is CharLiteral ck)
                                normKey = "c:" + ck.Value;
                            else if (keyExpr is BooleanLiteral bk)
                                normKey = "b:" + (bk.Value ? "true" : "false");

                            if (normKey != null)
                            {
                                if (seenLiteralKeys.Contains(normKey))
                                {
                                    _errors.Add($"Hash literal: clave duplicada '{normKey}' en la línea {keyExpr.Line}, columna {keyExpr.Column}.");
                                }
                                else
                                {
                                    seenLiteralKeys.Add(normKey);
                                }
                            }

                            var kType = GetExpressionType(keyExpr);
                            var vType = GetExpressionType(valExpr);

                            if (inferredKeyType == null)
                                inferredKeyType = kType;
                            else if (!TypeEquals(inferredKeyType, kType))
                                inferredKeyType = new UnknownType();

                            if (inferredValueType == null)
                                inferredValueType = vType;
                            else if (!TypeEquals(inferredValueType, vType))
                                inferredValueType = new UnknownType();
                        }

                        return new HashType(inferredKeyType ?? new UnknownType(), inferredValueType ?? new UnknownType());
                    }

                // ---------- Indexación ----------
                case Monkey.AST.Expressions.IndexExpression idxExpr:
                    {
                        var leftType = GetExpressionType(idxExpr.Left);
                        var indexType = GetExpressionType(idxExpr.Index);

                        // arrays
                        if (leftType is ArrayType at)
                        {
                            if (!TypeEquals(indexType, new IntType()))
                                _errors.Add("Índice de array debe ser int.");
                            return at.ElementType;
                        }

                        // string -> char
                        if (leftType is StringType)
                        {
                            if (!TypeEquals(indexType, new IntType()))
                                _errors.Add("Índice de string debe ser int.");
                            return new CharType();
                        }

                        // hash -> valueType
                        if (leftType is HashType ht)
                        {
                            if (!TypeEquals(indexType, ht.KeyType) && !(ht.KeyType is UnknownType))
                                _errors.Add("Tipo de clave incompatible al indexar hash.");
                            return ht.ValueType;
                        }

                        _errors.Add("Indexación sobre tipo no indexable.");
                        return new UnknownType();
                    }

                // ---------- Identificadores ----------
                case Identifier id:
                    return ResolveIdentifierType(id);

                // ---------- Binarias ----------
                case BinaryExpression bin:
                    return GetBinaryType(bin);

                // ---------- Literal de función ----------
                case FunctionLiteral fl:
                    return GetFunctionLiteralType(fl);

                // ---------- Llamada a función ----------
                case CallExpression call:
                    return VisitFunctionCall(call);

                default:
                    return new UnknownType();
            }
        }

        private IType GetFunctionLiteralType(FunctionLiteral fl)
        {
            // Scope para parámetros del literal de función
            _localScopes.Push(new Dictionary<string, IType>());
            foreach (var p in fl.Parameters)
                _localScopes.Peek()[p.Name] = p.Type;

            // Validar returns dentro del literal
            _returnTypeStack.Push(fl.ReturnType);
            VisitNode(fl.Body);
            _returnTypeStack.Pop();
            _localScopes.Pop();

            var ft = new FunctionType(fl.ReturnType);
            foreach (var p in fl.Parameters)
                ft.ParameterTypes.Add(p.Type);

            return ft;
        }

        private IType VisitFunctionCall(CallExpression call)
        {
            // Resolver tipo del identificador (puede venir de scope local o global)
            var calleeType = ResolveIdentifierType(new Identifier(call.FunctionName));

            if (calleeType is not FunctionType ft)
            {
                _errors.Add($"'{call.FunctionName}' no es una función.");
                return new UnknownType();
            }

            if (call.Arguments.Count != ft.ParameterTypes.Count)
            {
                _errors.Add($"La función '{call.FunctionName}' esperaba {ft.ParameterTypes.Count} argumentos, pero se pasaron {call.Arguments.Count}.");
                return ft.ReturnType;
            }

            // Verificar tipos de argumentos
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var argType = GetExpressionType(call.Arguments[i]);
                var expected = ft.ParameterTypes[i];

                if (expected is UnknownType)
                    continue;

                if (!TypeEquals(argType, expected))
                {
                    _errors.Add(
                        $"Argumento #{i + 1} de '{call.FunctionName}' es {argType.GetType().Name} pero se esperaba {expected.GetType().Name}."
                    );
                }
            }

            // Builtins con retorno desconocido en la tabla
            if (ft.ReturnType is UnknownType)
            {
                switch (call.FunctionName)
                {
                    case "len":
                        return new IntType();
                    case "first":
                    case "last":
                        {
                            var t0 = GetExpressionType(call.Arguments[0]);
                            if (t0 is ArrayType at) return at.ElementType;
                            if (t0 is StringType) return new CharType();
                            return new UnknownType();
                        }
                    case "rest":
                        {
                            var t0 = GetExpressionType(call.Arguments[0]);
                            if (t0 is ArrayType at) return new ArrayType(at.ElementType);
                            return new UnknownType();
                        }
                    case "push":
                        {
                            var t0 = GetExpressionType(call.Arguments[0]);
                            if (t0 is ArrayType at) return new ArrayType(at.ElementType);
                            return new UnknownType();
                        }
                }
            }

            return ft.ReturnType;
        }

        private IType GetBinaryType(BinaryExpression bin)
        {
            var lt = GetExpressionType(bin.Left);
            var rt = GetExpressionType(bin.Right);

            if (!TypeEquals(lt, rt))
                _errors.Add("Operación entre tipos incompatibles.");

            return bin.Operator switch
            {
                BinaryOperator.Add => lt,
                BinaryOperator.Subtract => lt,
                BinaryOperator.Multiply => lt,
                BinaryOperator.Divide => lt,
                BinaryOperator.Greater => new BoolType(),
                BinaryOperator.Less => new BoolType(),
                BinaryOperator.Equal => new BoolType(),
                _ => new UnknownType(),
            };
        }

        private IType ResolveIdentifierType(Identifier id)
        {
            // Buscar en scopes locales
            foreach (var scope in _localScopes)
                if (scope.TryGetValue(id.Value, out var t))
                    return t;

            // Buscar en tabla global
            if (_symbols.Symbols.TryGetValue(id.Value, out var t2))
                return t2;

            _errors.Add($"Variable no declarada: '{id.Value}'.");
            return new UnknownType();
        }

        private bool TypeEquals(IType a, IType b)
        {
            if (a == null || b == null) return false;

            // Igualdad simple por tipo de runtime (suficiente para este proyecto)
            return a.GetType() == b.GetType();
        }

        // ============================================================
        // DETECCIÓN ESTRUCTURAL DE RETURN
        // ============================================================
        private bool ContainsReturn(BlockStatement block)
        {
            foreach (var stmt in block.Statements)
            {
                switch (stmt)
                {
                    case ReturnStatement:
                        return true;

                    case BlockStatement b:
                        if (ContainsReturn(b)) return true;
                        break;

                    case IfStatement ifs:
                        bool thenHas = ContainsReturn(ifs.Consequence);
                        bool elseHas = ifs.Alternative != null && ContainsReturn(ifs.Alternative);
                        if (thenHas && elseHas)
                            return true;
                        break;

                    case WhileStatement:
                        // while con return garantizado es complejo → no garantizamos
                        break;
                }
            }
            return false;
        }
    }
}

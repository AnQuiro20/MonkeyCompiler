using Monkey.AST;
using Monkey.AST.Statements;
using Monkey.AST.Expressions;
using Monkey.AST.Types;
using Monkey.Common;
using System;
using System.Collections.Generic;

namespace Monkey.SymbolTable
{
    public class SymbolTableBuilderVisitor
    {
        // === ÁMBITOS JERÁRQUICOS ===
        private readonly SymbolTable _global = new SymbolTable(null, "global");
        private SymbolTable _current;

        // === Tabla global para TypeChecker (funciones + vars globales) ===
        private readonly Dictionary<string, IType> _globals = new();

        // === Errores ===
        private readonly List<string> _errors = new();
        public IReadOnlyList<string> Errors => _errors.AsReadOnly();

        public SymbolTableBuilderVisitor()
        {
            _current = _global;
            RegisterBuiltins();
        }

        // =========================================================
        // Builtins (se registran como funciones en el scope global)
        // =========================================================
        private void RegisterBuiltins()
        {
            // len(x) -> int
            var lenType = new FunctionType(new IntType());
            lenType.ParameterTypes.Add(new UnknownType());
            _globals["len"] = lenType;

            // first(x) -> tipo elemento
            var firstType = new FunctionType(new UnknownType());
            firstType.ParameterTypes.Add(new UnknownType());
            _globals["first"] = firstType;

            var lastType = new FunctionType(new UnknownType());
            lastType.ParameterTypes.Add(new UnknownType());
            _globals["last"] = lastType;

            var restType = new FunctionType(new UnknownType());
            restType.ParameterTypes.Add(new UnknownType());
            _globals["rest"] = restType;

            var pushType = new FunctionType(new UnknownType());
            pushType.ParameterTypes.Add(new UnknownType());
            pushType.ParameterTypes.Add(new UnknownType());
            _globals["push"] = pushType;
        }

        public SymbolTable GlobalTable => _global;

        // =========================================================
        // VISITADOR PRINCIPAL
        // =========================================================
        public void VisitNode(IASTNode? node)
        {
            if (node == null)
                return;

            switch (node)
            {
                case Monkey.AST.Program p:
                    foreach (var d in p.Declarations)
                        VisitNode(d);

                    VisitNode(p.MainFunction);

                    foreach (var stmt in p.Statements)
                        VisitNode(stmt);
                    break;

                case FunctionDeclaration func:
                    RegisterFunction(func);
                    break;

                case LetStatement letStmt:
                    RegisterVariable(letStmt);
                    break;

                case BlockStatement block:
                    EnterScope("block");
                    foreach (var s in block.Statements)
                        VisitNode(s);
                    ExitScope();
                    break;

                case ExpressionStatement es:
                    VisitNode(es.Expression);
                    break;
            }
        }

        // =========================================================
        // REGISTRO DE FUNCIONES
        // =========================================================
        private void RegisterFunction(FunctionDeclaration f)
        {
            if (_current.Resolve(f.Name.Value) != null || _globals.ContainsKey(f.Name.Value))
            {
                _errors.Add($"La función '{f.Name.Value}' ya fue declarada.");
                return;
            }

            var fnType = new FunctionType(f.ReturnType);
            foreach (var p in f.Parameters)
                fnType.ParameterTypes.Add(p.Type);

            // Guardar en scope global
            _globals[f.Name.Value] = fnType;

            // Registrar símbolo global
            var fnInfo = new SymbolInfo(f.Name.Value, fnType.GetType().Name, f.Line, f.Column, isFunction: true);
            _global.Define(fnInfo);

            Console.WriteLine($"✅ Función registrada: {f.Name.Value} (FunctionType)");

            // Nuevo scope para parámetros + locales
            EnterScope($"function {f.Name.Value}");

            foreach (var p in f.Parameters)
            {
                var pInfo = new SymbolInfo(p.Name, p.Type.GetType().Name, p.Line, p.Column);
                if (!_current.Define(pInfo))
                    _errors.Add($"Parámetro '{p.Name}' duplicado en la función '{f.Name.Value}'.");
            }

            VisitNode(f.Body);

            ExitScope();
        }

        // =========================================================
        // REGISTRO DE VARIABLES
        // =========================================================
        private void RegisterVariable(LetStatement let)
        {
            var type = let.Type ?? new IntType();

            // prohibir sombra en el MISMO bloque
            if (!_current.Define(new SymbolInfo(let.Identifier.Value, type.GetType().Name, let.Line, let.Column)))
            {
                _errors.Add(
                    $"La variable '{let.Identifier.Value}' ya fue declarada en este mismo ámbito."
                );
                return;
            }

            // Agregar también al diccionario global solo si estamos en el ámbito global
            if (_current == _global)
                _globals[let.Identifier.Value] = type;

            Console.WriteLine($"✅ Variable registrada: {let.Identifier.Value} ({type.GetType().Name}) en ámbito {_current.Name}");
        }

        // =========================================================
        // SCOPES
        // =========================================================
        private void EnterScope(string name)
        {
            var child = new SymbolTable(_current, name);
            _current = child;
        }

        private void ExitScope()
        {
            if (_current.Parent != null)
                _current = _current.Parent;
        }

        // =========================================================
        // ACCESO EXTERNO (para TypeChecker)
        // =========================================================
        public IReadOnlyDictionary<string, IType> Symbols => _globals;

        public void PrintSymbols()
        {
            Console.WriteLine("\n📜 Tabla de símbolos (jerarquía de ámbitos):");
            PrintScope(_global, 0);
        }

        private void PrintScope(SymbolTable scope, int indent)
        {
            var pad = new string(' ', indent * 2);
            Console.WriteLine($"{pad}- Ámbito: {scope.Name}");

            foreach (var s in scope.GetAllSymbols())
                Console.WriteLine($"{pad}  * {s}");

            foreach (var child in scope.Children)
                PrintScope(child, indent + 1);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Monkey.Common;
using Monkey.AST.Expressions;
using Monkey.AST.Statements;

namespace Monkey.AST.Visualization
{
    // Simple DOT generator for the AST. Traverses nodes and emits nodes/edges.
    public class AstDotGenerator
    {
        private readonly StringBuilder _sb = new();
        private readonly Dictionary<IASTNode, string> _ids = new(new ReferenceEqualityComparer());
        private int _nextId = 1;

        public string Generate(IASTNode root)
        {
            _sb.Clear();
            _ids.Clear();
            _nextId = 1;

            _sb.AppendLine("digraph AST {");
            _sb.AppendLine("  node [shape=box, fontname=\"Consolas\"];\n");

            EmitNodeRecursive(root);

            _sb.AppendLine("}");
            return _sb.ToString();
        }

        private string GetId(IASTNode node)
        {
            if (_ids.TryGetValue(node, out var id)) return id;
            id = "n" + (_nextId++).ToString();
            _ids[node] = id;
            return id;
        }

        private void Emit(string s) => _sb.AppendLine(s);

        private void EmitNodeRecursive(IASTNode node)
        {
            if (node == null) return;
            var id = GetId(node);
            var label = NodeLabel(node);
            Emit($"  {id} [label=\"{Escape(label)}\"];\n");

            // Emit children depending on node type
            switch (node)
            {
                case Monkey.AST.Program program:
                    foreach (var d in program.Declarations)
                        EmitEdge(id, d, "decl");
                    if (program.MainFunction != null)
                        EmitEdge(id, program.MainFunction, "main");
                    foreach (var s in program.Statements)
                        EmitEdge(id, s, "stmt");
                    break;

                case FunctionDeclaration f:
                    EmitEdge(id, f.Body, "body");
                    break;

                case BlockStatement b:
                    foreach (var s in b.Statements)
                        EmitEdge(id, s, "stmt");
                    break;

                case LetStatement let:
                    EmitEdge(id, let.Value, "value");
                    break;

                case ExpressionStatement es:
                    EmitEdge(id, es.Expression, "expr");
                    break;

                case BinaryExpression bin:
                    EmitEdge(id, bin.Left, "left");
                    EmitEdge(id, bin.Right, "right");
                    break;

                case Identifier ident:
                    // leaf
                    break;

                case IntegerLiteral il:
                    break;

                case StringLiteral sl:
                    break;

                case BooleanLiteral bl:
                    break;

                case PrintStatement ps:
                    EmitEdge(id, ps.Expression, "expr");
                    break;

                case IfStatement iff:
                    EmitEdge(id, iff.Condition, "cond");
                    EmitEdge(id, iff.Consequence, "then");
                    if (iff.Alternative != null)
                        EmitEdge(id, iff.Alternative, "else");
                    break;

                case WhileStatement w:
                    EmitEdge(id, w.Condition, "cond");
                    EmitEdge(id, w.Body, "body");
                    break;

                case CallExpression call:
                    // show function name as attribute node
                    // create synthetic node for function name
                    var fnNode = new SyntheticNode("fn:" + call.FunctionName);
                    var fnId = GetId(fnNode);
                    Emit($"  {fnId} [label=\"{Escape(fnNode.Label)}\", style=rounded];\n");
                    Emit($"  {id} -> {fnId} [label=\"callee\"];\n");
                    foreach (var a in call.Arguments)
                        EmitEdge(id, a, "arg");
                    break;

                case ArrayLiteral arr:
                    foreach (var e in arr.Elements)
                        EmitEdge(id, e, "el");
                    break;

                case HashLiteral h:
                    foreach (var kv in h.Pairs)
                    {
                        // kv is a KeyValuePair<IExpression,IExpression> like tuple
                        EmitEdge(id, kv.Key, "key");
                        EmitEdge(id, kv.Value, "value");
                    }
                    break;

                case FunctionLiteral fl:
                    EmitEdge(id, fl.Body, "body");
                    break;

                default:
                    // fallback: reflect for properties
                    EmitChildrenByReflection(node, id);
                    break;
            }

            // Recursively emit children nodes that we referenced
            // (children emitted inside EmitEdge)
        }

        private void EmitChildrenByReflection(IASTNode node, string id)
        {
            var t = node.GetType();
            foreach (var prop in t.GetProperties())
            {
                if (typeof(IASTNode).IsAssignableFrom(prop.PropertyType))
                {
                    var child = prop.GetValue(node) as IASTNode;
                    if (child != null) EmitEdge(id, child, prop.Name.ToLower());
                }
                else if (typeof(IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                {
                    var val = prop.GetValue(node) as IEnumerable;
                    if (val == null) continue;
                    foreach (var item in val)
                    {
                        if (item is IASTNode an)
                            EmitEdge(id, an, prop.Name.ToLower());
                    }
                }
            }
        }

        private void EmitEdge(IASTNode parent, IASTNode child, string label)
        {
            var pid = GetId(parent);
            var cid = GetId(child);
            // ensure child node emitted
            if (!_ids.ContainsValue(cid))
            {
                // child not yet emitted: emit minimal placeholder now
                Emit($"  {cid} [label=\"{Escape(NodeLabel(child))}\"];\n");
            }
            Emit($"  {pid} -> {cid} [label=\"{Escape(label)}\"];\n");
            // recurse into child
            EmitNodeRecursive(child);
        }

        private void EmitEdge(string parentId, IASTNode child, string label)
        {
            // helper if needed
            var cid = GetId(child);
            Emit($"  {parentId} -> {cid} [label=\"{Escape(label)}\"];\n");
            EmitNodeRecursive(child);
        }

        private string NodeLabel(IASTNode node)
        {
            switch (node)
            {
                case Monkey.AST.Program _:
                    return "Program";
                case FunctionDeclaration f:
                    return $"Function: {f.Name.Value}";
                case BlockStatement _:
                    return "Block";
                case LetStatement l:
                    return $"Let {l.Identifier.Value}";
                case ExpressionStatement _:
                    return "ExpressionStmt";
                case BinaryExpression b:
                    return $"Binary {b.Operator}";
                case Identifier id:
                    return $"Id: {id.Value}";
                case IntegerLiteral i:
                    return $"Int: {i.Value}";
                case StringLiteral s:
                    return $"Str: {s.Value}";
                case BooleanLiteral bo:
                    return $"Bool: {bo.Value}";
                case PrintStatement _:
                    return "Print";
                case IfStatement _:
                    return "If";
                case WhileStatement _:
                    return "While";
                case CallExpression c:
                    return "Call";
                case ArrayLiteral _:
                    return "ArrayLit";
                case HashLiteral _:
                    return "HashLit";
                case FunctionLiteral _:
                    return "FuncLit";
                default:
                    return node.GetType().Name;
            }
        }

        private static string Escape(string s)
        {
            return s.Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        private class SyntheticNode : IASTNode
        {
            public string Label { get; }
            public SyntheticNode(string label) => Label = label;
            public int Line { get; set; }
            public int Column { get; set; }
            public override string ToString() => Label;
        }

        // Reference-based equality comparer so dictionary keys are identity-based
        private sealed class ReferenceEqualityComparer : IEqualityComparer<IASTNode>
        {
            public bool Equals(IASTNode? x, IASTNode? y) => ReferenceEquals(x, y);
            public int GetHashCode(IASTNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

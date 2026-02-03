using System;
using System.Text;
using Monkey.AST;
using Monkey.AST.Expressions;
using Monkey.AST.Statements;
using Monkey.Common;

namespace Monkey.Frontend
{
    public class ASTPrinterVisitor
    {
        // New pretty tree printer using ├─ └─ characters and small summaries
        private readonly StringBuilder _sb = new();

        public string Print(IASTNode? node)
        {
            if (node == null) return "(nulo)";
            _sb.Clear();

            // Provide a compact program header elsewhere; this method returns the tree body.
            PrintNode(node, "", true);
            return _sb.ToString();
        }

        private void PrintNode(IASTNode node, string prefix, bool isLast)
        {
            var marker = prefix.Length == 0 ? "" : (isLast ? "└── " : "├── ");
            _sb.AppendLine(prefix + marker + NodeSummary(node));

            // Prepare child list
            var children = GetChildren(node);
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var childPrefix = prefix + (prefix.Length == 0 ? "" : (isLast ? "    " : "│   "));
                PrintNode(child, childPrefix, i == children.Count - 1);
            }
        }

        private List<IASTNode> GetChildren(IASTNode node)
        {
            var res = new List<IASTNode>();
            switch (node)
            {
                case Monkey.AST.Program p:
                    if (p.MainFunction != null) res.Add(p.MainFunction);
                    res.AddRange(p.Declarations);
                    res.AddRange(p.Statements);
                    break;
                case FunctionDeclaration f:
                    if (f.Body != null) res.Add(f.Body);
                    break;
                case BlockStatement b:
                    res.AddRange(b.Statements);
                    break;
                case LetStatement l:
                    if (l.Value != null) res.Add(l.Value);
                    break;
                case ExpressionStatement es:
                    if (es.Expression != null) res.Add(es.Expression);
                    break;
                case BinaryExpression be:
                    res.Add(be.Left);
                    res.Add(be.Right);
                    break;
                case PrintStatement ps:
                    if (ps.Expression != null) res.Add(ps.Expression);
                    break;
                case IfStatement iff:
                    if (iff.Condition != null) res.Add(iff.Condition);
                    if (iff.Consequence != null) res.Add(iff.Consequence);
                    if (iff.Alternative != null) res.Add(iff.Alternative);
                    break;
                case WhileStatement w:
                    if (w.Condition != null) res.Add(w.Condition);
                    if (w.Body != null) res.Add(w.Body);
                    break;
                case CallExpression call:
                    res.AddRange(call.Arguments);
                    break;
                case ArrayLiteral arr:
                    res.AddRange(arr.Elements);
                    break;
                case HashLiteral h:
                    foreach (var kv in h.Pairs)
                    {
                        res.Add(kv.Key);
                        res.Add(kv.Value);
                    }
                    break;
                case FunctionLiteral fl:
                    if (fl.Body != null) res.Add(fl.Body);
                    break;
                default:
                    // fallback: reflect for properties that are IASTNode or IEnumerable of IASTNode
                    var t = node.GetType();
                    foreach (var prop in t.GetProperties())
                    {
                        if (typeof(IASTNode).IsAssignableFrom(prop.PropertyType))
                        {
                            var val = prop.GetValue(node) as IASTNode;
                            if (val != null) res.Add(val);
                        }
                        else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType) && prop.PropertyType != typeof(string))
                        {
                            var val = prop.GetValue(node) as System.Collections.IEnumerable;
                            if (val == null) continue;
                            foreach (var item in val)
                            {
                                if (item is IASTNode an) res.Add(an);
                            }
                        }
                    }
                    break;
            }
            return res;
        }

        private string NodeSummary(IASTNode node)
        {
            switch (node)
            {
                case Monkey.AST.Program _:
                    return "Program";
                case FunctionDeclaration f:
                    return $"FunctionDeclaration: {f.Name.Value}";
                case BlockStatement _:
                    return "BlockStatement";
                case LetStatement l:
                    var val = l.Value is Monkey.AST.Expressions.IntegerLiteral il ? il.Value.ToString() : l.Value?.GetType().Name ?? "<expr>";
                    return $"LetStatement: {l.Identifier.Value} = {val}";
                case ExpressionStatement _:
                    return "ExpressionStatement";
                case BinaryExpression b:
                    return $"BinaryExpression: {b.Operator}";
                case Identifier id:
                    return $"Identifier: {id.Value}";
                case IntegerLiteral i:
                    return $"IntegerLiteral: {i.Value}";
                case StringLiteral s:
                    return $"StringLiteral: \"{s.Value}\"";
                case BooleanLiteral bo:
                    return $"BooleanLiteral: {bo.Value}";
                case PrintStatement _:
                    return "PrintStatement";
                case IfStatement _:
                    return "IfStatement";
                case WhileStatement _:
                    return "WhileStatement";
                case CallExpression c:
                    return $"Call: {c.FunctionName}({string.Join(", ", c.Arguments.ConvertAll(a => a.GetType().Name))})";
                case ArrayLiteral _:
                    return "ArrayLiteral";
                case HashLiteral _:
                    return "HashLiteral";
                case FunctionLiteral _:
                    return "FunctionLiteral";
                default:
                    return node.GetType().Name;
            }
        }
    }
}

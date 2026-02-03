using System;
using System.Collections.Generic;
using Monkey.AST;
using Monkey.AST.Expressions;
using Monkey.AST.Statements;
using Monkey.AST.Types;
using Monkey.Common;

namespace Monkey.CodeGeneration
{
    public class CodeGeneratorVisitor
    {
        private readonly List<string> _instructions = new();
        private int _labelCounter = 0;

        public IReadOnlyList<string> Instructions => _instructions;

        public void VisitNode(IASTNode? node)
        {
            if (node == null) return;
            // Emit a source-line marker for better runtime diagnostics if available.
            if (node is Monkey.Common.IASTNode astNode && astNode.Line > 0)
            {
                _instructions.Add($"// LINE {astNode.Line} {node.GetType().Name}");
            }

            switch (node)
            {
                case Monkey.AST.Program program:
                    foreach (var decl in program.Declarations)
                        VisitNode(decl);
                    VisitNode(program.MainFunction);
                    foreach (var stmt in program.Statements)
                        VisitNode(stmt);
                    break;

                case FunctionDeclaration func:
                    var paramNames = string.Join(" ", func.Parameters.ConvertAll(p => p.Name));
                    _instructions.Add($"FUNC_START {func.Name.Value} {paramNames}");
                    VisitNode(func.Body);
                    _instructions.Add("RET"); // asegura retorno implícito
                    _instructions.Add($"FUNC_END {func.Name.Value}");
                    break;

                case LetStatement letStmt:
                    _instructions.Add($"DECLARE {letStmt.Identifier.Value} : {letStmt.Type?.GetType().Name ?? "UnknownType"}");
                    VisitNode(letStmt.Value);
                    _instructions.Add($"STORE {letStmt.Identifier.Value}");
                    break;

                case ExpressionStatement exprStmt:
                    VisitNode(exprStmt.Expression);
                    _instructions.Add("POP");
                    break;

                case IntegerLiteral intLit:
                    _instructions.Add($"LOAD_CONST {intLit.Value}");
                    break;

                case StringLiteral strLit:
                    _instructions.Add($"LOAD_CONST \"{strLit.Value}\"");
                    break;

                case BooleanLiteral boolLit:
                    _instructions.Add($"LOAD_CONST {(boolLit.Value ? "true" : "false")}");
                    break;

                case Identifier id:
                    _instructions.Add($"LOAD_VAR {id.Value}");
                    break;

                case BinaryExpression bin:
                    VisitNode(bin.Left);
                    VisitNode(bin.Right);
                    switch (bin.Operator)
                    {
                        case BinaryOperator.Add: _instructions.Add("ADD"); break;
                        case BinaryOperator.Subtract: _instructions.Add("SUB"); break;
                        case BinaryOperator.Multiply: _instructions.Add("MUL"); break;
                        case BinaryOperator.Divide: _instructions.Add("DIV"); break;
                        case BinaryOperator.Less: _instructions.Add("LT"); break;
                        case BinaryOperator.Greater: _instructions.Add("GT"); break;
                        case BinaryOperator.LessOrEqual: _instructions.Add("LTE"); break;
                        case BinaryOperator.GreaterOrEqual: _instructions.Add("GTE"); break;
                        case BinaryOperator.Equal: _instructions.Add("EQ"); break;
                        default: _instructions.Add($"// Op no implementado: {bin.Operator}"); break;
                    }
                    break;

                case CallExpression call:
                    foreach (var arg in call.Arguments)
                        VisitNode(arg);
                    _instructions.Add($"CALL {call.FunctionName} {call.Arguments.Count}");
                    break;

                case IndexExpression idx:
                    // Simple placeholder: evaluate left and index, then emit LOAD_INDEX.
                    // Runtime currently treats LOAD_INDEX as returning a default (0).
                    VisitNode(idx.Left);
                    VisitNode(idx.Index);
                    _instructions.Add("LOAD_INDEX");
                    break;

                case ArrayLiteral arrLit:
                    // Emit code to push each element, then create an array of N elements
                    foreach (var el in arrLit.Elements)
                        VisitNode(el);
                    _instructions.Add($"ARRAY_CREATE {arrLit.Elements.Count}");
                    break;

                case ReturnStatement ret:
                    if (ret.Expression != null)
                        VisitNode(ret.Expression);
                    _instructions.Add("RET");
                    break;

                case PrintStatement ps:
                    VisitNode(ps.Expression);
                    _instructions.Add("PRINT");
                    break;

                case BlockStatement block:
                    foreach (var stmt in block.Statements)
                        VisitNode(stmt);
                    break;

                case IfStatement ifs:
                    {
                        string elseLbl = $"else_{_labelCounter++}";
                        string endLbl = $"endif_{_labelCounter++}";

                        VisitNode(ifs.Condition);
                        _instructions.Add($"JUMP_IF_FALSE {elseLbl}");

                        VisitNode(ifs.Consequence);
                        _instructions.Add($"JUMP {endLbl}");

                        _instructions.Add($"LABEL {elseLbl}");
                        if (ifs.Alternative != null)
                            VisitNode(ifs.Alternative);
                        _instructions.Add($"LABEL {endLbl}");
                        break;
                    }
                
                case WhileStatement ws:
                {
                    string startLbl = $"while_start_{_labelCounter++}";
                    string endLbl   = $"while_end_{_labelCounter++}";

                    _instructions.Add($"LABEL {startLbl}");
                    VisitNode(ws.Condition);
                    _instructions.Add($"JUMP_IF_FALSE {endLbl}");
                    VisitNode(ws.Body);
                    _instructions.Add($"JUMP {startLbl}");
                    _instructions.Add($"LABEL {endLbl}");
                    break;
                }


                default:
                    _instructions.Add($"// No implementado: {node.GetType().Name}");
                    break;
            }
        }

        public void Print()
        {
            Console.WriteLine("\n⚙️  Código intermedio generado:");
            foreach (var instr in _instructions)
                Console.WriteLine($"  {instr}");
        }
    }
}

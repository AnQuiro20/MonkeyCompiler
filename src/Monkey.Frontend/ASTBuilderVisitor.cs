using ProgramNode = Monkey.AST.Program;
using Monkey.Common;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Monkey.AST;
using Monkey.AST.Expressions;
using Monkey.AST.Statements;
using Monkey.AST.Types;
using Antlr4.Runtime.Misc;

namespace Monkey.Frontend;

public class ASTBuilderVisitor : MonkeyBaseVisitor<IASTNode>
{
    public override IASTNode VisitProgram(MonkeyParser.ProgramContext context)
    {
        var program = new ProgramNode
        {
            Line = context.Start.Line,
            Column = context.Start.Column
        };

        foreach (var funcDecl in context.functionDeclaration())
            program.Declarations.Add(Visit(funcDecl));

        foreach (var stmt in context.statement())
        {
            var node = Visit(stmt);
            if (node != null)
                program.Statements.Add(node);
        }

        if (context.mainFunction() != null)
            program.MainFunction = Visit(context.mainFunction());

        return program;
    }

    public override IASTNode VisitFunctionDeclaration(MonkeyParser.FunctionDeclarationContext ctx)
    {
        var name = new Identifier(ctx.IDENTIFIER().GetText())
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };

        var parameters = new List<Parameter>();
        if (ctx.functionParameters() != null)
        {
            foreach (var p in ctx.functionParameters().parameter())
            {
                var type = ParseType(p.type());
                parameters.Add(new Parameter(p.IDENTIFIER().GetText(), type)
                {
                    Line = p.Start.Line,
                    Column = p.Start.Column
                });
            }
        }

        var returnType = ParseType(ctx.type());
        var body = Visit(ctx.blockStatement()) as BlockStatement;

        return new FunctionDeclaration(name, parameters, returnType, body!)
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };
    }

    public override IASTNode VisitMainFunction(MonkeyParser.MainFunctionContext ctx)
    {
        var body = Visit(ctx.blockStatement()) as BlockStatement;
        return new FunctionDeclaration(new Identifier("main"), new List<Parameter>(), new VoidType(), body!)
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };
    }

    public override IASTNode VisitBlockStatement(MonkeyParser.BlockStatementContext ctx)
    {
        var block = new BlockStatement
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };

        foreach (var stmt in ctx.statement())
        {
            var node = Visit(stmt);
            if (node is Monkey.AST.Statements.IStatement statement)
                block.Statements.Add(statement);
        }

        return block;
    }

    public override IASTNode VisitLetStatement(MonkeyParser.LetStatementContext context)
    {
        var identifier = new Identifier(context.IDENTIFIER().GetText())
        {
            Line = context.IDENTIFIER().Symbol.Line,
            Column = context.IDENTIFIER().Symbol.Column
        };

        var type = ParseType(context.type());
        var value = Visit(context.expression()) as IExpression ?? new IntegerLiteral(0);

        return new LetStatement(identifier, type, value)
        {
            Line = context.Start.Line,
            Column = context.Start.Column
        };
    }

    public override IASTNode VisitElementExpression(MonkeyParser.ElementExpressionContext ctx)
    {
        var current = Visit(ctx.primitiveExpression());

        foreach (var child in ctx.children)
        {
            if (child is MonkeyParser.CallExpressionContext callCtx)
            {
                if (current is Identifier id)
                {
                    var args = new List<IExpression>();
                    if (callCtx.expressionList() != null)
                    {
                        foreach (var ex in callCtx.expressionList().expression())
                        {
                            var argNode = Visit(ex);
                            if (argNode is IExpression expr)
                                args.Add(expr);
                        }
                    }

                    current = new CallExpression(id.Value, args)
                    {
                        Line = callCtx.Start.Line,
                        Column = callCtx.Start.Column
                    };
                }
            }
            else if (child is MonkeyParser.ElementAccessContext accessCtx)
            {
                // element access: [ expression ]
                var idxNode = Visit(accessCtx.expression()) as IExpression ?? new IntegerLiteral(0);
                if (current is IExpression leftExpr)
                {
                    current = new IndexExpression(leftExpr, idxNode)
                    {
                        Line = accessCtx.Start.Line,
                        Column = accessCtx.Start.Column
                    };
                }
            }
        }

        return current!;
    }

    public override IASTNode VisitArrayLiteral(MonkeyParser.ArrayLiteralContext ctx)
    {
        var arr = new ArrayLiteral { Line = ctx.Start.Line, Column = ctx.Start.Column };
        if (ctx.expressionList() != null)
        {
            foreach (var e in ctx.expressionList().expression())
            {
                var node = Visit(e);
                if (node is IExpression expr) arr.Elements.Add(expr);
            }
        }
        return arr;
    }

    public override IASTNode VisitHashLiteral(MonkeyParser.HashLiteralContext ctx)
    {
        var h = new HashLiteral { Line = ctx.Start.Line, Column = ctx.Start.Column };
        foreach (var c in ctx.hashContent())
        {
            var k = Visit(c.expression(0)) as IExpression ?? new IntegerLiteral(0);
            var v = Visit(c.expression(1)) as IExpression ?? new IntegerLiteral(0);
            h.Pairs[k] = v;
        }
        return h;
    }

    public override IASTNode VisitFunctionLiteral(MonkeyParser.FunctionLiteralContext ctx)
    {
        var parameters = new List<Parameter>();
        if (ctx.functionParameters() != null)
        {
            foreach (var p in ctx.functionParameters().parameter())
            {
                var type = ParseType(p.type());
                parameters.Add(new Parameter(p.IDENTIFIER().GetText(), type) { Line = p.Start.Line, Column = p.Start.Column });
            }
        }
        var returnType = ParseType(ctx.type());
        var body = Visit(ctx.blockStatement()) as BlockStatement ?? new BlockStatement();
        var fl = new FunctionLiteral(returnType, body) { Line = ctx.Start.Line, Column = ctx.Start.Column };
        foreach (var p in parameters) fl.Parameters.Add(p);
        return fl;
    }

    public override IASTNode VisitPrimitiveExpression(MonkeyParser.PrimitiveExpressionContext ctx)
    {
        if (ctx.INTEGER() != null)
            return new IntegerLiteral(long.Parse(ctx.INTEGER().GetText())) { Line = ctx.Start.Line, Column = ctx.Start.Column };

        if (ctx.STRING_LITERAL() != null)
            return new StringLiteral(ctx.STRING_LITERAL().GetText().Trim('"')) { Line = ctx.Start.Line, Column = ctx.Start.Column };

        if (ctx.CHAR_LITERAL() != null)
            return new CharLiteral(ctx.CHAR_LITERAL().GetText().Trim('\'')[0]) { Line = ctx.Start.Line, Column = ctx.Start.Column };

        if (ctx.TRUE() != null || ctx.FALSE() != null)
            return new BooleanLiteral(ctx.TRUE() != null) { Line = ctx.Start.Line, Column = ctx.Start.Column };

        if (ctx.IDENTIFIER() != null)
            return new Identifier(ctx.IDENTIFIER().GetText()) { Line = ctx.Start.Line, Column = ctx.Start.Column };

        if (ctx.arrayLiteral() != null)
            return Visit(ctx.arrayLiteral());

        if (ctx.hashLiteral() != null)
            return Visit(ctx.hashLiteral());

        if (ctx.functionLiteral() != null)
            return Visit(ctx.functionLiteral());

        if (ctx.expression() != null)
            return Visit(ctx.expression());

        return new IntegerLiteral(0);
    }

    private IType ParseType(MonkeyParser.TypeContext? ctx)
    {
        if (ctx == null) return new VoidType();

        // Primitive tokens
        if (ctx.INT() != null) return new IntType();
        if (ctx.STRING() != null) return new StringType();
        if (ctx.BOOL() != null) return new BoolType();
        if (ctx.CHAR() != null) return new CharType();
        if (ctx.VOID() != null) return new VoidType();

        // Array type: array< T >
        if (ctx.arrayType() != null)
        {
            var inner = ParseType(ctx.arrayType().type());
            var at = new ArrayType(inner)
            {
                Line = ctx.Start.Line,
                Column = ctx.Start.Column
            };
            return at;
        }

        // Hash type: hash< K, V >
        if (ctx.hashType() != null)
        {
            var types = ctx.hashType().type();
            var kt = ParseType(types[0]);
            var vt = ParseType(types[1]);
            var ht = new HashType(kt, vt)
            {
                Line = ctx.Start.Line,
                Column = ctx.Start.Column
            };
            return ht;
        }

        // Function type: fn ( params ) : return
        if (ctx.functionType() != null)
        {
            var ftCtx = ctx.functionType();
            var returnT = ParseType(ftCtx.type());
            var ft = new FunctionType(returnT)
            {
                Line = ctx.Start.Line,
                Column = ctx.Start.Column
            };

            if (ftCtx.functionParameterTypes() != null)
            {
                foreach (var tctx in ftCtx.functionParameterTypes().type())
                    ft.ParameterTypes.Add(ParseType(tctx));
            }

            return ft;
        }

        return new UnknownType();
    }

    public override IASTNode VisitReturnStatement(MonkeyParser.ReturnStatementContext ctx)
    {
        IExpression? expr = null;

        if (ctx.expression() != null)
        {
            var node = Visit(ctx.expression());
            if (node is IExpression e)
                expr = e;
        }

        return new ReturnStatement(expr)
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };
    }

    public override IASTNode VisitPrintStatement(MonkeyParser.PrintStatementContext ctx)
    {
        var exprNode = Visit(ctx.expression());
        return new PrintStatement(exprNode as IExpression ?? new IntegerLiteral(0))
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };
    }
    public override IASTNode VisitExpression(MonkeyParser.ExpressionContext ctx)
    {
        // expression: comparison;
        return Visit(ctx.comparison());
    }

    public override IASTNode VisitComparison(MonkeyParser.ComparisonContext ctx)
    {
        // comparison: additionExpression ((LT | GT | LTE | GTE | EQ) additionExpression)*;
        var left = Visit(ctx.additionExpression(0)) as IExpression;
        if (ctx.additionExpression().Length == 1) return left!;

        for (int i = 1; i < ctx.additionExpression().Length; i++)
        {
            var opToken = ctx.GetChild(2 * i - 1).GetText();
            var right = Visit(ctx.additionExpression(i)) as IExpression;
            var binOp = opToken switch
            {
                "<" => BinaryOperator.Less,
                ">" => BinaryOperator.Greater,
                "<=" => BinaryOperator.LessOrEqual,
                ">=" => BinaryOperator.GreaterOrEqual,
                "==" => BinaryOperator.Equal,
                _ => BinaryOperator.Equal
            };
            left = new BinaryExpression(left!, binOp, right!);
        }

        return left!;
    }

    public override IASTNode VisitAdditionExpression(MonkeyParser.AdditionExpressionContext ctx)
    {
        // additionExpression: multiplicationExpression ((PLUS | MINUS) multiplicationExpression)*;
        var left = Visit(ctx.multiplicationExpression(0)) as IExpression;
        if (ctx.multiplicationExpression().Length == 1) return left!;

        for (int i = 1; i < ctx.multiplicationExpression().Length; i++)
        {
            var opToken = ctx.GetChild(2 * i - 1).GetText();
            var right = Visit(ctx.multiplicationExpression(i)) as IExpression;
            var binOp = opToken == "+" ? BinaryOperator.Add : BinaryOperator.Subtract;
            left = new BinaryExpression(left!, binOp, right!);
        }

        return left!;
    }

    public override IASTNode VisitMultiplicationExpression(MonkeyParser.MultiplicationExpressionContext ctx)
    {
        // multiplicationExpression: elementExpression ((MULT | DIV) elementExpression)*;
        var left = Visit(ctx.elementExpression(0)) as IExpression;
        if (ctx.elementExpression().Length == 1) return left!;

        for (int i = 1; i < ctx.elementExpression().Length; i++)
        {
            var opToken = ctx.GetChild(2 * i - 1).GetText();
            var right = Visit(ctx.elementExpression(i)) as IExpression;
            var binOp = opToken == "*" ? BinaryOperator.Multiply : BinaryOperator.Divide;
            left = new BinaryExpression(left!, binOp, right!);
        }

        return left!;
    }

    public override IASTNode VisitIfStatement(MonkeyParser.IfStatementContext ctx)
    {
        var condition = Visit(ctx.expression()) as IExpression;
        var consequence = Visit(ctx.blockStatement(0)) as BlockStatement;
        BlockStatement? alternative = null;

        if (ctx.blockStatement().Length > 1)
            alternative = Visit(ctx.blockStatement(1)) as BlockStatement;

        return new IfStatement(condition!, consequence!, alternative)
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };
    }

    public override IASTNode VisitWhileStatement(MonkeyParser.WhileStatementContext ctx)
    {
        var condition = Visit(ctx.expression()) as IExpression;
        var body = Visit(ctx.blockStatement()) as BlockStatement;

        return new WhileStatement(condition!, body!)
        {
            Line = ctx.Start.Line,
            Column = ctx.Start.Column
        };
    }



}

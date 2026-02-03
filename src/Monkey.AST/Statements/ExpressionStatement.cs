using Monkey.Common;
using Monkey.AST.Expressions;

namespace Monkey.AST.Statements;

public class ExpressionStatement : IStatement
{
    public IExpression Expression { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }

    public ExpressionStatement(IExpression expression)
    {
        Expression = expression;
    }

    public override string ToString() => Expression.ToString() ?? "";
}

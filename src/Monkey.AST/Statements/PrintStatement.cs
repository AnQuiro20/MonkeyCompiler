using Monkey.Common;
using Monkey.AST.Expressions;

namespace Monkey.AST.Statements
{
    public class PrintStatement : IStatement
    {
        public IExpression Expression { get; }
        public int Line { get; set; }
        public int Column { get; set; }

        public PrintStatement(IExpression expr)
        {
            Expression = expr;
        }
    }
}

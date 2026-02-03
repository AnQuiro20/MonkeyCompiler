using Monkey.Common;
using Monkey.AST.Expressions;

namespace Monkey.AST.Statements
{
    public class WhileStatement : IStatement
    {
        public IExpression Condition { get; }
        public BlockStatement Body { get; }
        public int Line { get; set; }
        public int Column { get; set; }

        public WhileStatement(IExpression condition, BlockStatement body)
        {
            Condition = condition;
            Body = body;
        }
    }
}

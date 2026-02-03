using Monkey.Common;
using Monkey.AST.Expressions;

namespace Monkey.AST.Statements
{
    public class IfStatement : IStatement
    {
        public IExpression Condition { get; set; }
        public BlockStatement Consequence { get; set; }
        public BlockStatement? Alternative { get; set; }

        public int Line { get; set; }
        public int Column { get; set; }

        public IfStatement(IExpression condition, BlockStatement consequence, BlockStatement? alternative = null)
        {
            Condition = condition;
            Consequence = consequence;
            Alternative = alternative;
        }

        public override string ToString() =>
            $"if ({Condition}) {Consequence}" + (Alternative != null ? $" else {Alternative}" : "");
    }
}

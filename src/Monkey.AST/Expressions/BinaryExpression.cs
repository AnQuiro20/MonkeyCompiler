using Monkey.Common;

namespace Monkey.AST.Expressions
{
    public enum BinaryOperator
    {
        Add, Subtract, Multiply, Divide,
        Less, Greater, LessOrEqual, GreaterOrEqual, Equal
    }

    public class BinaryExpression : IExpression
    {
        public IExpression Left { get; }
        public IExpression Right { get; }
        public BinaryOperator Operator { get; }

        public int Line { get; set; }
        public int Column { get; set; }

        public BinaryExpression(IExpression left, BinaryOperator op, IExpression right)
        {
            Left = left;
            Operator = op;
            Right = right;
        }
    }
}

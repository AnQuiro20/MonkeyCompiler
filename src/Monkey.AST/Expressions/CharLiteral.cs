using Monkey.Common;

namespace Monkey.AST.Expressions;

public class CharLiteral : IExpression
{
    public char Value { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public CharLiteral(char value)
    {
        Value = value;
    }
}

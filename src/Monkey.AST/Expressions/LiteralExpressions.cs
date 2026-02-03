using Monkey.Common;

namespace Monkey.AST.Expressions;

public class IntegerLiteral : IExpression
{
    public long Value { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public IntegerLiteral(long value)
    {
        Value = value;
    }
}

public class StringLiteral : IExpression
{
    public string Value { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public StringLiteral(string value)
    {
        Value = value;
    }
}

public class BooleanLiteral : IExpression
{
    public bool Value { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public BooleanLiteral(bool value)
    {
        Value = value;
    }
}

public class Identifier : IExpression
{
    public string Value { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public Identifier(string value)
    {
        Value = value;
    }
}

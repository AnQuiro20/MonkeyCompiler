using Monkey.Common;
using Monkey.AST.Statements;

namespace Monkey.AST.Expressions;


public class ArrayLiteral : IExpression
{
    public List<IExpression> Elements { get; } = new();
    public int Line { get; set; }
    public int Column { get; set; }
}

public class HashLiteral : IExpression
{
    public Dictionary<IExpression, IExpression> Pairs { get; } = new();
    public int Line { get; set; }
    public int Column { get; set; }
}

public class IndexExpression : IExpression
{
    public IExpression Left { get; }
    public IExpression Index { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public IndexExpression(IExpression left, IExpression index)
    {
        Left = left;
        Index = index;
    }
}

public class FunctionLiteral : IExpression
{
    public List<Parameter> Parameters { get; } = new();
    public IType ReturnType { get; }
    public BlockStatement Body { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public FunctionLiteral(IType returnType, BlockStatement body)
    {
        ReturnType = returnType;
        Body = body;
    }
}

public class Parameter
{
    public string Name { get; }
    public IType Type { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public Parameter(string name, IType type)
    {
        Name = name;
        Type = type;
    }
}

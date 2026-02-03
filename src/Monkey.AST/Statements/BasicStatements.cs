using Monkey.Common;
using Monkey.AST.Expressions;
using Monkey.AST.Types;

namespace Monkey.AST.Statements;

public interface IStatement : IASTNode { }

public class LetStatement : IStatement
{
    public Identifier Identifier { get; set; }
    public IType Type { get; set; }
    public IExpression Value { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }

    public LetStatement(Identifier identifier, IType type, IExpression value)
    {
        Identifier = identifier;
        Type = type;
        Value = value;
    }

    public override string ToString() =>
        $"let {Identifier.Value}: {Type} = {Value}";
}

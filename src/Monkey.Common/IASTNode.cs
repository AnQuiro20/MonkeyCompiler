namespace Monkey.Common;

public interface IASTNode
{
    int Line { get; set; }
    int Column { get; set; }
}

public interface IExpression : IASTNode { }

public interface IStatement : IASTNode { }

public interface IType : IASTNode { }

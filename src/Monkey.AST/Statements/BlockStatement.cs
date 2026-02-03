using Monkey.Common;

namespace Monkey.AST.Statements;

public class BlockStatement : IStatement
{
    public List<IStatement> Statements { get; } = new();
    public int Line { get; set; }
    public int Column { get; set; }
}

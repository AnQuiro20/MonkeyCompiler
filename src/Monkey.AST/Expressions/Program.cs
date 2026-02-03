using Monkey.Common;
using Monkey.AST.Statements;

namespace Monkey.AST;

public class Program : IASTNode
{
    public int Line { get; set; }
    public int Column { get; set; }

    public List<IASTNode> Declarations { get; } = new();
    public List<IASTNode> Statements { get; } = new();
    public IASTNode? MainFunction { get; set; }

    public override string ToString() => "Program";
}

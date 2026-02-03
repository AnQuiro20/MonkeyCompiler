using Monkey.Common;

namespace Monkey.AST.Types
{
    public class UnknownType : IType
    {
        public int Line { get; set; }
        public int Column { get; set; }

        public override string ToString() => "UnknownType";
    }
}

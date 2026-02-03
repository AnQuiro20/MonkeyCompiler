using Monkey.Common;

namespace Monkey.AST.Types;

public class IntType : IType
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public class StringType : IType
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public class BoolType : IType
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public class CharType : IType
{
    public int Line { get; set; }
    public int Column { get; set; }
}

public class VoidType : IType
{
    public int Line { get; set; }
    public int Column { get; set; }
}

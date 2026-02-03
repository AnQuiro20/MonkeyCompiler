using Monkey.Common;

namespace Monkey.AST.Types;

public class ArrayType : IType
{
    public IType ElementType { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public ArrayType(IType elementType)
    {
        ElementType = elementType;
    }
}

public class HashType : IType
{
    public IType KeyType { get; }
    public IType ValueType { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public HashType(IType keyType, IType valueType)
    {
        KeyType = keyType;
        ValueType = valueType;
    }
}

public class FunctionType : IType
{
    public List<IType> ParameterTypes { get; } = new();
    public IType ReturnType { get; }
    public int Line { get; set; }
    public int Column { get; set; }

    public FunctionType(IType returnType)
    {
        ReturnType = returnType;
    }
}

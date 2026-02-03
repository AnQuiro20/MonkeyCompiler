using Monkey.Common;
using Monkey.AST.Expressions;
using Monkey.AST.Types;

namespace Monkey.AST.Statements
{
    public class FunctionDeclaration : IStatement
    {
        public Identifier Name { get; }
        public List<Parameter> Parameters { get; }
        public IType ReturnType { get; }
        public BlockStatement Body { get; }

        public int Line { get; set; }
        public int Column { get; set; }

        public FunctionDeclaration(Identifier name, List<Parameter> parameters, IType returnType, BlockStatement body)
        {
            Name = name;
            Parameters = parameters;
            ReturnType = returnType;
            Body = body;
        }
    }

    public class ReturnStatement : IStatement
    {
        public IExpression Expression { get; }
        public int Line { get; set; }
        public int Column { get; set; }
        public object Value { get; set; }

        public ReturnStatement(IExpression expression)
        {
            Expression = expression;
        }
    }
}

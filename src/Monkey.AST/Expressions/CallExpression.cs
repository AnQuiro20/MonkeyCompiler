using System.Collections.Generic;
using Monkey.Common;

namespace Monkey.AST.Expressions
{
    // Versión simple: el callee es un identificador y guardamos su nombre
    public class CallExpression : IExpression
    {
        public object Function;

        public string FunctionName { get; }
        public List<IExpression> Arguments { get; } = new();

        public int Line { get; set; }
        public int Column { get; set; }
        public object Callee { get; set; }

        public CallExpression(string functionName, List<IExpression> args)
        {
            FunctionName = functionName;
            Arguments = args;
        }
    }
}

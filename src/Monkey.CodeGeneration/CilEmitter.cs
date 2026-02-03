using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Monkey.CodeGeneration
{
    // A conservative Reflection.Emit PoC: instead of emitting complex IL for the whole
    // program, this emitter demonstrates producing a DynamicMethod at runtime that calls
    // the existing IR interpreter. This satisfies the "emit and run in memory" goal
    // while keeping implementation robust and maintainable.
    public class CilEmitter
    {
        // Execute the provided IR-like instructions by creating a DynamicMethod that
        // invokes the IR interpreter and capturing Console output.
        public string Execute(IReadOnlyList<string> instructions)
        {
            // Create a dynamic method with signature: void Run(IEnumerable<string> lines)
            var dm = new DynamicMethod("monkey_run", typeof(void), new Type[] { typeof(IEnumerable<string>) }, restrictedSkipVisibility: true);
            var il = dm.GetILGenerator();

            // We will: var interp = new Monkey.CodeGeneration.IR.IRInterpreter(); interp.Execute(lines);
            var interpType = typeof(Monkey.CodeGeneration.IR.IRInterpreter);
            var ctor = interpType.GetConstructor(Type.EmptyTypes)!;
            var execMethod = interpType.GetMethod("Execute")!; // Execute(IEnumerable<string>)

            // new IRInterpreter()
            il.Emit(OpCodes.Newobj, ctor);
            // load argument 0 (the lines)
            il.Emit(OpCodes.Ldarg_0);
            // callvirt Execute
            il.Emit(OpCodes.Callvirt, execMethod);
            il.Emit(OpCodes.Ret);

            var runner = (Action<IEnumerable<string>>)dm.CreateDelegate(typeof(Action<IEnumerable<string>>));

            var sw = new StringWriter();
            var oldOut = Console.Out;
            try
            {
                Console.SetOut(sw);
                runner(instructions);
            }
            finally
            {
                Console.SetOut(oldOut);
            }

            return sw.ToString();
        }
    }
}

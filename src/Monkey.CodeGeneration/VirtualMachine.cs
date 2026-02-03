using System;
using System.Collections.Generic;

namespace Monkey.CodeGeneration
{
    public class VirtualMachine
    {
        private readonly List<string> _instructions;
        private readonly Stack<object> _stack = new();
        private readonly Stack<Dictionary<string, object>> _frames = new();
        private readonly Stack<int> _returnIps = new();
        private readonly Dictionary<string, (int start, int end, List<string> paramsList)> _functions = new();

        public VirtualMachine(List<string> instructions)
        {
            _instructions = instructions;
        }

        public void Execute()
        {
            Console.WriteLine("\n▶️ Ejecutando código intermedio...");
                // 1️⃣ Mapear funciones
                for (int i = 0; i < _instructions.Count; i++)
                {
                    if (_instructions[i].StartsWith("FUNC_START"))
                    {
                        var parts = _instructions[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var fname = parts[1];
                        var paramsList = new List<string>(parts.Length > 2 ? parts[2..] : Array.Empty<string>());
                        int j = i + 1;
                        while (j < _instructions.Count && !_instructions[j].StartsWith("FUNC_END"))
                            j++;
                        _functions[fname] = (i, j, paramsList);
                    }
                }

                _frames.Push(new Dictionary<string, object>()); // global
                int ip = 0;
                while (ip < _instructions.Count)
                {
                    var instr = _instructions[ip];
                    var raw = instr?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(raw)) { ip++; continue; }
                    // Ignore comment/meta lines emitted by the IR (e.g. "// LINE N ...")
                    if (raw.StartsWith("//")) { ip++; continue; }
                    try
                    {
                        var parts = raw.Split(' ', 2);
                        var op = parts[0];
                        var arg = parts.Length > 1 ? parts[1] : null;

                        switch (op)
                        {
                        case "FUNC_START":
                            {
                                // Si encontramos la definición de una función en el flujo normal,
                                // saltamos su cuerpo; las funciones deben ejecutarse solo vía CALL.
                                var partsStart = _instructions[ip].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                var fnameStart = partsStart.Length > 1 ? partsStart[1] : "";
                                if (_functions.TryGetValue(fnameStart, out var f))
                                {
                                    ip = f.end + 1;
                                    continue;
                                }
                                break;
                            }
                        case "FUNC_END":
                            break;

                        case "DECLARE":
                            var name = arg!.Split(':')[0].Trim();
                            // Only create the variable if it doesn't already exist in the current frame.
                            // This avoids overwriting variables on inner redeclarations during simple single-frame execution,
                            // matching the IRInterpreter behaviour which effectively ignores DECLARE.
                            if (!_frames.Peek().ContainsKey(name))
                                _frames.Peek()[name] = 0L;
                            break;

                        case "LOAD_CONST":
                            var val = arg!;
                            if (long.TryParse(val, out var num)) _stack.Push(num);
                            else _stack.Push(val.Trim('"'));
                            break;

                        case "STORE":
                            var vname = arg!;
                            _frames.Peek()[vname] = _stack.Pop();
                            break;

                        case "LOAD_VAR":
                            var lname = arg!;
                            if (!_frames.Peek().TryGetValue(lname, out var lval))
                                throw new Exception($"Variable '{lname}' no definida.");
                            _stack.Push(lval);
                            break;

                        case "ADD":
                        case "SUB":
                        case "MUL":
                        case "DIV":
                            var b = Convert.ToInt64(_stack.Pop());
                            var a = Convert.ToInt64(_stack.Pop());
                            _stack.Push(op switch
                            {
                                "ADD" => a + b,
                                "SUB" => a - b,
                                "MUL" => a * b,
                                "DIV" => b != 0 ? a / b : throw new DivideByZeroException(),
                                _ => 0L
                            });
                            break;

                        case "LT":
                        case "GT":
                        case "LTE":
                        case "GTE":
                        case "EQ":
                            {
                                var rb = _stack.Pop();
                                var ra = _stack.Pop();
                                // numeric comparisons where possible
                                if (ra is long la && rb is long lb)
                                {
                                    bool res = op switch
                                    {
                                        "LT" => la < lb,
                                        "GT" => la > lb,
                                        "LTE" => la <= lb,
                                        "GTE" => la >= lb,
                                        "EQ" => la == lb,
                                        _ => false
                                    };
                                    _stack.Push(res);
                                }
                                else
                                {
                                    // fallback to object equality for EQ, false otherwise
                                    if (op == "EQ") _stack.Push(object.Equals(ra, rb));
                                    else _stack.Push(false);
                                }
                                break;
                            }

                        case "CALL":
                            var callParts = arg!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var fname = callParts[0];
                            var argc = int.Parse(callParts[1]);
                            if (!_functions.TryGetValue(fname, out var func))
                            {
                                // Pop args for builtin handling (preserve original order)
                                var args = new object[argc];
                                for (int ai = argc - 1; ai >= 0; ai--)
                                    args[ai] = _stack.Pop();

                                if (HandleBuiltin(fname, args))
                                {
                                    // builtin handled and pushed result(s) onto stack
                                    break;
                                }

                                throw new Exception($"Función '{fname}' no encontrada.");
                            }

                            var newFrame = new Dictionary<string, object>();
                            for (int i = func.paramsList.Count - 1; i >= 0; i--)
                                newFrame[func.paramsList[i]] = _stack.Pop();
                            _frames.Push(newFrame);
                            _returnIps.Push(ip);
                            ip = func.start + 1;
                            continue;

                        case "RET":
                            var retVal = _stack.Count > 0 ? _stack.Pop() : 0L;
                            _frames.Pop();
                            ip = _returnIps.Pop() + 1;
                            _stack.Push(retVal);
                            continue;

                        case "PRINT":
                            var v = _stack.Pop();
                            // Pretty-print arrays and dictionaries
                            if (v is List<object> lst)
                            {
                                Console.WriteLine("[" + string.Join(", ", lst.ConvertAll(x => FormatValue(x))) + "]");
                            }
                            else if (v is Dictionary<string, object> dict)
                            {
                                var kvParts = new List<string>();
                                foreach (var kv in dict)
                                    kvParts.Add($"\"{kv.Key}\": {FormatValue(kv.Value)}");
                                Console.WriteLine("{" + string.Join(", ", kvParts) + "}");
                            }
                            else
                            {
                                Console.WriteLine(FormatValue(v));
                            }
                            break;

                        case "LOAD_INDEX":
                            {
                                // Pop index then container, then push the indexed value if possible.
                                object idxObj = null;
                                object container = null;
                                if (_stack.Count > 0) idxObj = _stack.Pop();
                                if (_stack.Count > 0) container = _stack.Pop();

                                // convert index to int if numeric
                                int index = -1;
                                if (idxObj is long l) index = (int)l;
                                else if (idxObj is int ii) index = ii;
                                else index = -1;

                                object result = 0L;
                                if (container is List<object> containerList)
                                {
                                    if (index >= 0 && index < containerList.Count)
                                        result = containerList[index];
                                    else
                                        result = 0L;
                                }
                                else if (container is string s)
                                {
                                    if (index >= 0 && index < s.Length)
                                        result = s[index].ToString();
                                    else
                                        result = string.Empty;
                                }
                                else
                                {
                                    result = 0L;
                                }

                                _stack.Push(result);
                                break;
                            }

                        case "LABEL":
                            // No ejecuta nada, es marcador de salto
                            break;

                        case "JUMP":
                            if (arg != null)
                            {
                                int idx = _instructions.FindIndex(i => i == $"LABEL {arg}");
                                if (idx != -1)
                                {
                                    ip = idx;
                                    continue;
                                }
                            }
                            break;

                        case "JUMP_IF_FALSE":
                            if (_stack.Count > 0)
                            {
                                var condObj = _stack.Pop();
                                bool isFalse = false;
                                if (condObj is bool bv) isFalse = bv == false;
                                else if (condObj is long ll) isFalse = ll == 0L;
                                else if (condObj is int ii) isFalse = ii == 0;
                                else if (condObj == null) isFalse = true;

                                if (isFalse && arg != null)
                                {
                                    int idx = _instructions.FindIndex(i => i == $"LABEL {arg}");
                                    if (idx != -1)
                                    {
                                        ip = idx;
                                        continue;
                                    }
                                }
                            }
                            break;


                        default:
                            Console.WriteLine($"⚠️ Instrucción desconocida: {instr}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"VM error at ip {ip}: '{instr}' -> {ex.Message}", ex);
                    }

                    ip++;
                }

                Console.WriteLine("✅ Ejecución completada.");
                Console.WriteLine("\n📦 Estado final de variables:");
                foreach (var kv in _frames.Peek())
                    Console.WriteLine($" - {kv.Key} = {kv.Value}");
        }

        private static string FormatValue(object? v)
        {
            if (v == null) return "null";
            if (v is string) return v.ToString()!;
            if (v is long || v is int) return v.ToString()!;
            if (v is bool b) return b ? "true" : "false";
            if (v is List<object> list) return "[" + string.Join(", ", list.ConvertAll(x => FormatValue(x))) + "]";
            if (v is Dictionary<string, object> dict)
            {
                var parts = new List<string>();
                foreach (var kv in dict) parts.Add($"\"{kv.Key}\": {FormatValue(kv.Value)}");
                return "{" + string.Join(", ", parts) + "}";
            }
            return v.ToString()!;
        }

        private bool HandleBuiltin(string name, object[] args)
        {
            try
            {
                switch (name)
                {
                    case "len":
                        if (args.Length != 1) { _stack.Push(0L); return true; }
                        var a = args[0];
                        if (a is string s) { _stack.Push((long)s.Length); return true; }
                        if (a is List<object> l) { _stack.Push((long)l.Count); return true; }
                        if (a is Dictionary<string, object> d) { _stack.Push((long)d.Count); return true; }
                        _stack.Push(0L);
                        return true;

                    case "first":
                        if (args.Length != 1) { _stack.Push(0L); return true; }
                        if (args[0] is List<object> ll && ll.Count > 0) { _stack.Push(ll[0]); return true; }
                        _stack.Push(0L);
                        return true;

                    case "last":
                        if (args.Length != 1) { _stack.Push(0L); return true; }
                        if (args[0] is List<object> lll && lll.Count > 0) { _stack.Push(lll[lll.Count - 1]); return true; }
                        _stack.Push(0L);
                        return true;

                    case "rest":
                        if (args.Length != 1) { _stack.Push(new List<object>()); return true; }
                        if (args[0] is List<object> lrest)
                        {
                            if (lrest.Count <= 1) { _stack.Push(new List<object>()); return true; }
                            var newList = new List<object>(lrest.GetRange(1, lrest.Count - 1));
                            _stack.Push(newList);
                            return true;
                        }
                        _stack.Push(new List<object>());
                        return true;

                    case "push":
                        if (args.Length != 2) { _stack.Push(new List<object>()); return true; }
                        if (args[0] is List<object> lpush)
                        {
                            var copy = new List<object>(lpush);
                            copy.Add(args[1]);
                            _stack.Push(copy);
                            return true;
                        }
                        // if first arg is not an array, create array with second arg
                        var single = new List<object> { args[1] };
                        _stack.Push(single);
                        return true;
                }
            }
            catch { /* fall-through to not-handled */ }
            return false;
        }
    }
}

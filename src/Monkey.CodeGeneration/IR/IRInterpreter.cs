using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monkey.CodeGeneration.IR
{
    /// <summary>
    /// Intérprete muy simple para el IR textual que emite tu CodeGeneration.
    /// Soporta:
    /// - FUNC_START name
    /// - FUNC_END name
    /// - LOAD_CONST <n|true|false|"texto">
    /// - STORE <id>
    /// - LOAD_VAR <id>
    /// - ADD, SUB, MUL, DIV       (enteros)
    /// - PRINT
    /// - POP
    /// </summary>
    public class IRInterpreter
    {
        private readonly Stack<object?> _stack = new();
        private readonly Stack<Dictionary<string, object?>> _frames = new();
        private readonly Stack<int> _returnIps = new();
        private readonly Dictionary<string, (int start, int end, List<string> paramsList)> _functions = new();

        public void Execute(IEnumerable<string> irLines)
        {
            // Materializa las líneas para poder indexarlas
            var lines = new List<string>(irLines);

            // 1) Mapea funciones (start, end, params)
            for (int i = 0; i < lines.Count; i++)
            {
                var raw = lines[i].Trim();
                if (!raw.StartsWith("FUNC_START")) continue;
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var fname = parts[1];
                var paramsList = new List<string>(parts.Length > 2 ? parts[2..] : Array.Empty<string>());
                int j = i + 1;
                while (j < lines.Count && !lines[j].Trim().StartsWith("FUNC_END")) j++;
                _functions[fname] = (i, j, paramsList);
            }

            // Frame global
            _frames.Push(new Dictionary<string, object?>());

            // 2) Mapea etiquetas (LABEL name -> indice)
            var labels = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i].Trim();
                if (l.StartsWith("LABEL "))
                {
                    var parts = l.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                        labels[parts[1]] = i;
                }
            }

            int ip = 0;
            while (ip < lines.Count)
            {
                var raw = lines[ip].Trim();
                if (string.IsNullOrWhiteSpace(raw)) { ip++; continue; }

                try
                {
                    var firstSpace = raw.IndexOf(' ');
                    var op = firstSpace < 0 ? raw : raw[..firstSpace];
                    var arg = firstSpace < 0 ? "" : raw[(firstSpace + 1)..].Trim();

                    switch (op)
                    {
                    case "FUNC_START":
                        {
                            // Si encontramos una definición de función en la ejecución normal,
                            // saltamos su cuerpo: no debe ejecutarse hasta que se llame.
                            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var fname = parts.Length > 0 ? parts[0] : arg;
                            if (_functions.TryGetValue(fname, out var func))
                            {
                                ip = func.end + 1;
                                continue;
                            }
                            break;
                        }
                    case "FUNC_END":
                        // no hacer nada
                        break;

                    case "LABEL":
                        // marcador; no hacer nada
                        break;

                    case "JUMP":
                        if (arg.Length > 0 && labels.TryGetValue(arg, out var jidx))
                        {
                            ip = jidx;
                            continue;
                        }
                        break;

                    case "JUMP_IF_FALSE":
                        {
                            var cond = _stack.Count > 0 ? _stack.Pop() : null;
                            var isFalse = cond == null || (cond is bool b && b == false) || (cond is int ii && ii == 0) || (cond is long ll && ll == 0L);
                            if (isFalse && arg.Length > 0 && labels.TryGetValue(arg, out var jidx2))
                            {
                                ip = jidx2;
                                continue;
                            }
                            break;
                        }

                    case "LOAD_CONST":
                        _stack.Push(ParseConst(arg));
                        break;

                    case "LOAD_INDEX":
                        {
                            // Pop index and container, perform indexing for arrays/strings
                            var idxVal = _stack.Count > 0 ? _stack.Pop() : null;
                            var leftVal = _stack.Count > 0 ? _stack.Pop() : null;
                            object? res = 0;
                            int idx = -1;
                            if (idxVal is long ll) idx = (int)ll;
                            else if (idxVal is int ii) idx = ii;

                            if (leftVal is List<object> listVal)
                            {
                                if (idx >= 0 && idx < listVal.Count) res = listVal[idx];
                                else res = 0;
                            }
                            else if (leftVal is string s)
                            {
                                if (idx >= 0 && idx < s.Length) res = s[idx].ToString();
                                else res = string.Empty;
                            }
                            else res = 0;
                            _stack.Push(res);
                            break;
                        }

                    case "ARRAY_CREATE":
                        {
                            // arg is number of elements
                            if (!int.TryParse(arg, out var n)) { _stack.Push(new List<object>()); break; }
                            var tmp = new List<object>(n);
                            for (int k = 0; k < n; k++)
                            {
                                if (_stack.Count == 0) tmp.Add(0);
                                else tmp.Add(_stack.Pop());
                            }
                            tmp.Reverse();
                            _stack.Push(tmp);
                            break;
                        }

                    case "POP":
                        if (_stack.Count > 0) _stack.Pop();
                        break;

                    case "STORE":
                        if (_stack.Count == 0) throw new InvalidOperationException("STORE sin valor en la pila");
                        _frames.Peek()[arg] = _stack.Pop();
                        break;

                    case "LOAD_VAR":
                        if (!_frames.Peek().TryGetValue(arg, out var val))
                            throw new KeyNotFoundException($"Variable '{arg}' no definida.");
                        _stack.Push(val);
                        break;

                    case "ADD":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a + b);
                            break;
                        }
                    case "SUB":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a - b);
                            break;
                        }
                    case "MUL":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a * b);
                            break;
                        }
                    case "DIV":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a / b);
                            break;
                        }

                    case "LT":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a < b);
                            break;
                        }
                    case "GT":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a > b);
                            break;
                        }
                    case "LTE":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a <= b);
                            break;
                        }
                    case "GTE":
                        {
                            var b = PopInt();
                            var a = PopInt();
                            _stack.Push(a >= b);
                            break;
                        }
                    case "EQ":
                        {
                            var b = _stack.Count > 0 ? _stack.Pop() : null;
                            var a = _stack.Count > 0 ? _stack.Pop() : null;
                            _stack.Push(object.Equals(a, b));
                            break;
                        }

                    case "PRINT":
                        {
                            var v = _stack.Count > 0 ? _stack.Pop() : null;
                            if (v is List<object> l)
                            {
                                Console.WriteLine("[" + string.Join(", ", l.ConvertAll(x => x?.ToString() ?? "null")) + "]");
                            }
                            else if (v is Dictionary<string, object?> d)
                            {
                                var parts = new List<string>();
                                foreach (var kv in d) parts.Add($"\"{kv.Key}\": {kv.Value?.ToString() ?? "null"}");
                                Console.WriteLine("{" + string.Join(", ", parts) + "}");
                            }
                            else
                            {
                                Console.WriteLine(v?.ToString() ?? "null");
                            }
                            break;
                        }

                    case "CALL":
                        {
                            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var fname = parts[0];
                            var argc = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                            if (!_functions.TryGetValue(fname, out var func))
                            {
                                // Try builtins
                                var args = new object?[argc];
                                for (int ai = argc - 1; ai >= 0; ai--) args[ai] = _stack.Pop();
                                if (HandleBuiltin(fname, args)) break;
                                throw new KeyNotFoundException($"Función '{fname}' no encontrada.");
                            }

                            var newFrame = new Dictionary<string, object?>();
                            // Pop args in reverse order (el codegen empuja en orden)
                            for (int k = func.paramsList.Count - 1; k >= 0; k--)
                            {
                                if (_stack.Count == 0) throw new InvalidOperationException("CALL con argumentos insuficientes en la pila");
                                newFrame[func.paramsList[k]] = _stack.Pop();
                            }
                            _frames.Push(newFrame);
                            _returnIps.Push(ip);
                            ip = func.start + 1;
                            continue; // salta el incremento de ip
                        }

                    case "RET":
                        {
                            var retVal = _stack.Count > 0 ? _stack.Pop() : null;
                            // Pop current frame
                            if (_frames.Count > 1) _frames.Pop();
                            if (_returnIps.Count == 0) // RET desde top-level
                            {
                                // empuja valor si había
                                if (retVal != null) _stack.Push(retVal);
                                ip++;
                                break;
                            }
                            ip = _returnIps.Pop() + 1;
                            if (retVal != null) _stack.Push(retVal);
                            continue;
                        }

                    default:
                        // Ignora instrucciones desconocidas
                        break;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"IR error at line {ip}: '{lines[ip].Trim()}' -> {ex.Message}", ex);
                }

                ip++;
            }
        }

        private int PopInt()
        {
            if (_stack.Count == 0) throw new InvalidOperationException("La pila está vacía.");
            var v = _stack.Pop();
            if (v is int i) return i;
            if (v is long l) return checked((int)l);
            throw new InvalidOperationException($"Se esperaba entero, vino: {v?.GetType().Name ?? "null"}");
        }

        private bool HandleBuiltin(string name, object?[] args)
        {
            try
            {
                switch (name)
                {
                    case "len":
                        if (args.Length != 1) { _stack.Push(0); return true; }
                        var a = args[0];
                        if (a is string s) { _stack.Push(s.Length); return true; }
                        if (a is List<object> l) { _stack.Push(l.Count); return true; }
                        if (a is Dictionary<string, object?> d) { _stack.Push(d.Count); return true; }
                        _stack.Push(0);
                        return true;

                    case "first":
                        if (args.Length != 1) { _stack.Push(0); return true; }
                        if (args[0] is List<object> lst && lst.Count > 0) { _stack.Push(lst[0]); return true; }
                        _stack.Push(0);
                        return true;

                    case "last":
                        if (args.Length != 1) { _stack.Push(0); return true; }
                        if (args[0] is List<object> llast && llast.Count > 0) { _stack.Push(llast[llast.Count - 1]); return true; }
                        _stack.Push(0);
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
                            copy.Add(args[1]!);
                            _stack.Push(copy);
                            return true;
                        }
                        var single = new List<object> { args[1]! };
                        _stack.Push(single);
                        return true;
                }
            }
            catch { }
            return false;
        }

        private object? ParseConst(string arg)
        {
            // Soporta enteros, bool, y strings con comillas
            if (arg.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (arg.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

            // String literal rodeado de comillas
            if ((arg.StartsWith('"') && arg.EndsWith('"')) ||
                (arg.StartsWith('\'') && arg.EndsWith('\'')))
            {
                // quita comillas
                return arg.Substring(1, arg.Length - 2);
            }

            // Int
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;

            // Long
            if (long.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return l;

            // Si no se reconoce, empuja el texto crudo
            return arg;
        }
    }
}

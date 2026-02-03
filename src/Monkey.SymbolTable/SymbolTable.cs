using System.Collections.Generic;

namespace Monkey.SymbolTable;

public class SymbolTable
{
    private readonly Dictionary<string, SymbolInfo> _symbols = new();
    private readonly List<SymbolTable> _children = new();
    public SymbolTable? Parent { get; }
    public string Name { get; }

    public SymbolTable(SymbolTable? parent = null, string name = "scope")
    {
        Parent = parent;
        Name = name;
        if (parent != null)
            parent._children.Add(this);
    }

    public bool Define(SymbolInfo symbol)
    {
        if (_symbols.ContainsKey(symbol.Name))
            return false; // duplicado
        _symbols[symbol.Name] = symbol;
        return true;
    }

    public SymbolInfo? Resolve(string name)
    {
        if (_symbols.TryGetValue(name, out var sym))
            return sym;

        return Parent?.Resolve(name);
    }

    public IEnumerable<SymbolInfo> GetAllSymbols() => _symbols.Values;

    public IReadOnlyList<SymbolTable> Children => _children.AsReadOnly();
}

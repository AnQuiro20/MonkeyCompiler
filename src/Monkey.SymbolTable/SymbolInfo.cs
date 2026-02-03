namespace Monkey.SymbolTable;

public class SymbolInfo
{
    public string Name { get; set; }
    public string Type { get; set; } = "unknown";
    public int Line { get; set; }
    public int Column { get; set; }
    public bool IsFunction { get; set; }

    public SymbolInfo(string name, string type, int line, int column, bool isFunction = false)
    {
        Name = name;
        Type = type;
        Line = line;
        Column = column;
        IsFunction = isFunction;
    }

    public override string ToString() =>
        $"{(IsFunction ? "func" : "var")} {Name}:{Type} (L{Line},C{Column})";
}

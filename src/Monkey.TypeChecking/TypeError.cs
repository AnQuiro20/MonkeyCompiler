namespace Monkey.TypeChecking
{
    public class TypeError
    {
        public int Line { get; }
        public int Column { get; }
        public string Message { get; }

        public TypeError(int line, int column, string message)
        {
            Line = line;
            Column = column;
            Message = message;
        }

        public override string ToString() =>
            $"[L{Line},C{Column}] {Message}";
    }
}

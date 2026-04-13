namespace Terminon.Terminal;

[Flags]
public enum CellAttributes : byte
{
    None        = 0,
    Bold        = 1 << 0,
    Italic      = 1 << 1,
    Underline   = 1 << 2,
    Blink       = 1 << 3,
    Reverse     = 1 << 4,
    Invisible   = 1 << 5,
    Strikethrough = 1 << 6,
    Dim         = 1 << 7,
}

public struct TerminalCell
{
    public char Character;
    public TerminalColor Foreground;
    public TerminalColor Background;
    public CellAttributes Attributes;

    public static readonly TerminalCell Empty = new()
    {
        Character = ' ',
        Foreground = TerminalColor.Default,
        Background = TerminalColor.Default,
        Attributes = CellAttributes.None
    };

    public bool IsEmpty =>
        Character == ' ' &&
        Foreground.IsDefault &&
        Background.IsDefault &&
        Attributes == CellAttributes.None;

    public bool HasAttribute(CellAttributes attr) => (Attributes & attr) != 0;
}

/// <summary>A single rendered line stored in the scrollback buffer.</summary>
public class TerminalLine
{
    private readonly TerminalCell[] _cells;

    public TerminalLine(int width)
    {
        _cells = new TerminalCell[width];
        for (int i = 0; i < width; i++)
            _cells[i] = TerminalCell.Empty;
    }

    public TerminalLine(TerminalCell[] cells)
    {
        _cells = (TerminalCell[])cells.Clone();
    }

    public int Width => _cells.Length;
    public TerminalCell this[int col] => col < _cells.Length ? _cells[col] : TerminalCell.Empty;
    public ReadOnlySpan<TerminalCell> Cells => _cells;
}

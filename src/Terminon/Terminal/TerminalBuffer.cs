using System.Collections.Concurrent;

namespace Terminon.Terminal;

/// <summary>
/// Thread-safe terminal screen buffer with scrollback.
/// All write operations are done from the VT100 parser thread.
/// Reads (for rendering) should call Snapshot().
/// </summary>
public sealed class TerminalBuffer
{
    private readonly object _lock = new();

    private int _columns;
    private int _rows;
    private TerminalCell[][] _screen;       // [row][col], active screen
    private TerminalCell[][]? _altScreen;   // alternate screen buffer

    private readonly List<TerminalLine> _scrollback = new();
    private int _maxScrollback;

    // Cursor state
    private int _cursorRow;
    private int _cursorCol;
    private bool _cursorVisible = true;

    // Saved cursor
    private (int row, int col, CellAttributes attrs, TerminalColor fg, TerminalColor bg) _savedCursor;
    private (int row, int col, CellAttributes attrs, TerminalColor fg, TerminalColor bg) _savedAltCursor;

    // Scroll region
    private int _scrollTop;
    private int _scrollBottom;

    // Current attributes
    private TerminalColor _currentFg = TerminalColor.Default;
    private TerminalColor _currentBg = TerminalColor.Default;
    private CellAttributes _currentAttrs = CellAttributes.None;

    // G0/G1 character sets (for line drawing etc.)
    private bool _useLineDrawing;

    // Modes
    private bool _insertMode;
    private bool _autoWrap = true;
    private bool _originMode;
    private bool _usingAltScreen;

    // Pending wrap (deferred wrap at end of line)
    private bool _pendingWrap;

    private int _viewportScroll; // 0 = bottom, positive = scrolled up (lines)

    public event EventHandler? BufferChanged;
    public event Action<string>? TitleChanged;
    public event Action? BellTriggered;

    public TerminalBuffer(int columns, int rows, int maxScrollback = 10_000)
    {
        _columns = columns;
        _rows = rows;
        _maxScrollback = maxScrollback;
        _screen = AllocScreen(rows, columns);
        _scrollTop = 0;
        _scrollBottom = rows - 1;
    }

    private static TerminalCell[][] AllocScreen(int rows, int cols)
    {
        var s = new TerminalCell[rows][];
        for (int r = 0; r < rows; r++)
        {
            s[r] = new TerminalCell[cols];
            for (int c = 0; c < cols; c++)
                s[r][c] = TerminalCell.Empty;
        }
        return s;
    }

    // ─── Public read properties ────────────────────────────────────────────────

    public int Columns { get { lock (_lock) return _columns; } }
    public int Rows { get { lock (_lock) return _rows; } }
    public int ScrollbackCount { get { lock (_lock) return _scrollback.Count; } }
    public int ViewportScroll { get { lock (_lock) return _viewportScroll; } set { lock (_lock) { _viewportScroll = Math.Clamp(value, 0, _scrollback.Count); } } }
    public bool CursorVisible { get { lock (_lock) return _cursorVisible; } }

    // ─── Resize ────────────────────────────────────────────────────────────────

    public void Resize(int cols, int rows)
    {
        lock (_lock)
        {
            if (cols == _columns && rows == _rows) return;
            var newScreen = AllocScreen(rows, cols);
            int copyRows = Math.Min(_rows, rows);
            int copyCols = Math.Min(_columns, cols);
            for (int r = 0; r < copyRows; r++)
                for (int c = 0; c < copyCols; c++)
                    newScreen[r][c] = _screen[r][c];
            _screen = newScreen;
            _columns = cols;
            _rows = rows;
            _cursorRow = Math.Clamp(_cursorRow, 0, rows - 1);
            _cursorCol = Math.Clamp(_cursorCol, 0, cols - 1);
            _scrollTop = 0;
            _scrollBottom = rows - 1;
        }
        NotifyChanged();
    }

    // ─── VT100 write operations (called from parser thread) ───────────────────

    public void WriteChar(char ch)
    {
        lock (_lock)
        {
            if (_pendingWrap && _autoWrap)
            {
                _cursorCol = 0;
                DoLineFeed(true);
                _pendingWrap = false;
            }

            if (_insertMode)
                InsertBlanksAt(_cursorRow, _cursorCol, 1);

            char c = _useLineDrawing ? MapLineDrawing(ch) : ch;
            SetCell(_cursorRow, _cursorCol, c);

            if (_cursorCol < _columns - 1)
                _cursorCol++;
            else
                _pendingWrap = true;
        }
        NotifyChanged();
    }

    public void Backspace()
    {
        lock (_lock)
        {
            _pendingWrap = false;
            if (_cursorCol > 0) _cursorCol--;
        }
        NotifyChanged();
    }

    public void Tab()
    {
        lock (_lock)
        {
            int next = ((_cursorCol / 8) + 1) * 8;
            _cursorCol = Math.Min(next, _columns - 1);
        }
        NotifyChanged();
    }

    public void LineFeed()
    {
        lock (_lock) { DoLineFeed(false); }
        NotifyChanged();
    }

    public void CarriageReturn()
    {
        lock (_lock) { _cursorCol = 0; _pendingWrap = false; }
        NotifyChanged();
    }

    public void Bell() => BellTriggered?.Invoke();

    // ─── CSI actions ──────────────────────────────────────────────────────────

    public void CursorUp(int n) { lock (_lock) { _pendingWrap = false; _cursorRow = Math.Max(_cursorRow - n, _originMode ? _scrollTop : 0); } NotifyChanged(); }
    public void CursorDown(int n) { lock (_lock) { _pendingWrap = false; _cursorRow = Math.Min(_cursorRow + n, _originMode ? _scrollBottom : _rows - 1); } NotifyChanged(); }
    public void CursorForward(int n) { lock (_lock) { _pendingWrap = false; _cursorCol = Math.Min(_cursorCol + n, _columns - 1); } NotifyChanged(); }
    public void CursorBack(int n) { lock (_lock) { _pendingWrap = false; _cursorCol = Math.Max(_cursorCol - n, 0); } NotifyChanged(); }

    public void CursorPosition(int row, int col)
    {
        lock (_lock)
        {
            _pendingWrap = false;
            int minRow = _originMode ? _scrollTop : 0;
            int maxRow = _originMode ? _scrollBottom : _rows - 1;
            _cursorRow = Math.Clamp(row - 1, minRow, maxRow);
            _cursorCol = Math.Clamp(col - 1, 0, _columns - 1);
        }
        NotifyChanged();
    }

    public void CursorNextLine(int n) { lock (_lock) { _cursorRow = Math.Min(_cursorRow + n, _rows - 1); _cursorCol = 0; } NotifyChanged(); }
    public void CursorPrevLine(int n) { lock (_lock) { _cursorRow = Math.Max(_cursorRow - n, 0); _cursorCol = 0; } NotifyChanged(); }
    public void CursorHorizontalAbsolute(int col) { lock (_lock) { _cursorCol = Math.Clamp(col - 1, 0, _columns - 1); } NotifyChanged(); }

    public void SaveCursor()
    {
        lock (_lock)
            _savedCursor = (_cursorRow, _cursorCol, _currentAttrs, _currentFg, _currentBg);
    }

    public void RestoreCursor()
    {
        lock (_lock)
        {
            (_cursorRow, _cursorCol, _currentAttrs, _currentFg, _currentBg) = _savedCursor;
            _cursorRow = Math.Clamp(_cursorRow, 0, _rows - 1);
            _cursorCol = Math.Clamp(_cursorCol, 0, _columns - 1);
        }
        NotifyChanged();
    }

    public void ShowCursor(bool show) { lock (_lock) { _cursorVisible = show; } NotifyChanged(); }

    public void SetScrollRegion(int top, int bottom)
    {
        lock (_lock)
        {
            _scrollTop = Math.Clamp(top - 1, 0, _rows - 2);
            _scrollBottom = Math.Clamp(bottom - 1, _scrollTop + 1, _rows - 1);
            if (_originMode) { _cursorRow = _scrollTop; _cursorCol = 0; }
        }
        NotifyChanged();
    }

    public void SetOriginMode(bool enabled)
    {
        lock (_lock) { _originMode = enabled; if (enabled) { _cursorRow = _scrollTop; _cursorCol = 0; } }
        NotifyChanged();
    }

    public void SetInsertMode(bool enabled) { lock (_lock) { _insertMode = enabled; } }
    public void SetAutoWrap(bool enabled) { lock (_lock) { _autoWrap = enabled; } }
    public void SetLineDrawing(bool enabled) { lock (_lock) { _useLineDrawing = enabled; } }

    public void EraseInDisplay(int mode)
    {
        lock (_lock)
        {
            switch (mode)
            {
                case 0: // From cursor to end
                    EraseLineFrom(_cursorRow, _cursorCol);
                    for (int r = _cursorRow + 1; r < _rows; r++) EraseLine(r);
                    break;
                case 1: // From start to cursor
                    for (int r = 0; r < _cursorRow; r++) EraseLine(r);
                    EraseLineTo(_cursorRow, _cursorCol);
                    break;
                case 2: // Entire screen
                    for (int r = 0; r < _rows; r++) EraseLine(r);
                    break;
                case 3: // Erase including scrollback
                    _scrollback.Clear();
                    for (int r = 0; r < _rows; r++) EraseLine(r);
                    break;
            }
        }
        NotifyChanged();
    }

    public void EraseInLine(int mode)
    {
        lock (_lock)
        {
            switch (mode)
            {
                case 0: EraseLineFrom(_cursorRow, _cursorCol); break;
                case 1: EraseLineTo(_cursorRow, _cursorCol); break;
                case 2: EraseLine(_cursorRow); break;
            }
        }
        NotifyChanged();
    }

    public void EraseCharacters(int n)
    {
        lock (_lock)
        {
            int end = Math.Min(_cursorCol + n, _columns);
            for (int c = _cursorCol; c < end; c++)
                _screen[_cursorRow][c] = TerminalCell.Empty;
        }
        NotifyChanged();
    }

    public void InsertLines(int n)
    {
        lock (_lock)
        {
            if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
            for (int i = 0; i < n; i++)
            {
                // Scroll lines from cursor to scroll bottom down
                for (int r = _scrollBottom; r > _cursorRow; r--)
                    _screen[r] = _screen[r - 1];
                _screen[_cursorRow] = new TerminalCell[_columns];
                for (int c = 0; c < _columns; c++) _screen[_cursorRow][c] = TerminalCell.Empty;
            }
        }
        NotifyChanged();
    }

    public void DeleteLines(int n)
    {
        lock (_lock)
        {
            if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
            for (int i = 0; i < n; i++)
            {
                for (int r = _cursorRow; r < _scrollBottom; r++)
                    _screen[r] = _screen[r + 1];
                _screen[_scrollBottom] = new TerminalCell[_columns];
                for (int c = 0; c < _columns; c++) _screen[_scrollBottom][c] = TerminalCell.Empty;
            }
        }
        NotifyChanged();
    }

    public void DeleteCharacters(int n)
    {
        lock (_lock)
        {
            int end = Math.Min(_cursorCol + n, _columns);
            for (int c = _cursorCol; c < _columns - n; c++)
                _screen[_cursorRow][c] = _screen[_cursorRow][c + n];
            for (int c = _columns - n; c < _columns; c++)
                _screen[_cursorRow][c] = TerminalCell.Empty;
        }
        NotifyChanged();
    }

    public void InsertBlanks(int n)
    {
        lock (_lock) { InsertBlanksAt(_cursorRow, _cursorCol, n); }
        NotifyChanged();
    }

    public void ScrollUp(int n)
    {
        lock (_lock)
        {
            for (int i = 0; i < n; i++) ScrollScreenUp();
        }
        NotifyChanged();
    }

    public void ScrollDown(int n)
    {
        lock (_lock)
        {
            for (int i = 0; i < n; i++) ScrollScreenDown();
        }
        NotifyChanged();
    }

    public void SetGraphicsRendition(List<int> parameters)
    {
        lock (_lock)
        {
            int i = 0;
            if (parameters.Count == 0) parameters = new List<int> { 0 };
            while (i < parameters.Count)
            {
                int p = parameters[i];
                switch (p)
                {
                    case 0: _currentAttrs = CellAttributes.None; _currentFg = TerminalColor.Default; _currentBg = TerminalColor.Default; break;
                    case 1: _currentAttrs |= CellAttributes.Bold; break;
                    case 2: _currentAttrs |= CellAttributes.Dim; break;
                    case 3: _currentAttrs |= CellAttributes.Italic; break;
                    case 4: _currentAttrs |= CellAttributes.Underline; break;
                    case 5: _currentAttrs |= CellAttributes.Blink; break;
                    case 7: _currentAttrs |= CellAttributes.Reverse; break;
                    case 8: _currentAttrs |= CellAttributes.Invisible; break;
                    case 9: _currentAttrs |= CellAttributes.Strikethrough; break;
                    case 22: _currentAttrs &= ~(CellAttributes.Bold | CellAttributes.Dim); break;
                    case 23: _currentAttrs &= ~CellAttributes.Italic; break;
                    case 24: _currentAttrs &= ~CellAttributes.Underline; break;
                    case 25: _currentAttrs &= ~CellAttributes.Blink; break;
                    case 27: _currentAttrs &= ~CellAttributes.Reverse; break;
                    case 28: _currentAttrs &= ~CellAttributes.Invisible; break;
                    case 29: _currentAttrs &= ~CellAttributes.Strikethrough; break;
                    case 39: _currentFg = TerminalColor.Default; break;
                    case 49: _currentBg = TerminalColor.Default; break;
                    case >= 30 and <= 37: _currentFg = TerminalColor.FromIndex(p - 30); break;
                    case >= 40 and <= 47: _currentBg = TerminalColor.FromIndex(p - 40); break;
                    case >= 90 and <= 97: _currentFg = TerminalColor.FromIndex(p - 90 + 8); break;
                    case >= 100 and <= 107: _currentBg = TerminalColor.FromIndex(p - 100 + 8); break;
                    case 38:
                        if (i + 2 < parameters.Count && parameters[i + 1] == 5)
                        { _currentFg = TerminalColor.FromIndex(parameters[i + 2]); i += 2; }
                        else if (i + 4 < parameters.Count && parameters[i + 1] == 2)
                        { _currentFg = TerminalColor.FromRGB((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]); i += 4; }
                        break;
                    case 48:
                        if (i + 2 < parameters.Count && parameters[i + 1] == 5)
                        { _currentBg = TerminalColor.FromIndex(parameters[i + 2]); i += 2; }
                        else if (i + 4 < parameters.Count && parameters[i + 1] == 2)
                        { _currentBg = TerminalColor.FromRGB((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]); i += 4; }
                        break;
                }
                i++;
            }
        }
    }

    public void SwitchToAltScreen()
    {
        lock (_lock)
        {
            if (_usingAltScreen) return;
            _savedCursor = (_cursorRow, _cursorCol, _currentAttrs, _currentFg, _currentBg);
            _altScreen = AllocScreen(_rows, _columns);
            (_screen, _altScreen) = (_altScreen, _screen);
            _usingAltScreen = true;
            _cursorRow = 0; _cursorCol = 0;
        }
        NotifyChanged();
    }

    public void SwitchToNormalScreen()
    {
        lock (_lock)
        {
            if (!_usingAltScreen) return;
            (_screen, _altScreen) = (_altScreen!, _screen);
            _altScreen = null;
            _usingAltScreen = false;
            (_cursorRow, _cursorCol, _currentAttrs, _currentFg, _currentBg) = _savedCursor;
        }
        NotifyChanged();
    }

    public void SetTitle(string title) => TitleChanged?.Invoke(title);

    // ─── Snapshot for rendering ────────────────────────────────────────────────

    public BufferSnapshot Snapshot()
    {
        lock (_lock)
        {
            int scrollCount = _scrollback.Count;
            int visibleScroll = Math.Min(_viewportScroll, scrollCount);
            int screenRowsNeeded = _rows;

            // Build a combined view from scrollback + screen
            var lines = new TerminalLine[_rows];

            if (visibleScroll == 0)
            {
                // Show active screen
                for (int r = 0; r < _rows; r++)
                    lines[r] = new TerminalLine(_screen[r]);
            }
            else
            {
                // Show scrollback
                int sbStart = scrollCount - visibleScroll;
                for (int r = 0; r < _rows; r++)
                {
                    int sbIndex = sbStart + r;
                    if (sbIndex < scrollCount)
                        lines[r] = _scrollback[sbIndex];
                    else
                        lines[r] = new TerminalLine(_screen[r - (scrollCount - sbStart)]);
                }
            }

            var cursorRow = visibleScroll == 0 ? _cursorRow : -1;
            return new BufferSnapshot(lines, _columns, _rows, cursorRow, _cursorCol,
                _cursorVisible && visibleScroll == 0, scrollCount, visibleScroll);
        }
    }

    // ─── Private helpers ───────────────────────────────────────────────────────

    private void DoLineFeed(bool fromWrap)
    {
        _pendingWrap = false;
        if (_cursorRow == _scrollBottom)
            ScrollScreenUp();
        else if (_cursorRow < _rows - 1)
            _cursorRow++;
    }

    private void ScrollScreenUp()
    {
        // Push top line to scrollback (only if not in alt screen and at scroll region top)
        if (!_usingAltScreen && _scrollTop == 0)
        {
            _scrollback.Add(new TerminalLine(_screen[_scrollTop]));
            if (_scrollback.Count > _maxScrollback)
                _scrollback.RemoveAt(0);
        }

        for (int r = _scrollTop; r < _scrollBottom; r++)
            _screen[r] = _screen[r + 1];

        _screen[_scrollBottom] = new TerminalCell[_columns];
        for (int c = 0; c < _columns; c++)
            _screen[_scrollBottom][c] = TerminalCell.Empty;
    }

    private void ScrollScreenDown()
    {
        for (int r = _scrollBottom; r > _scrollTop; r--)
            _screen[r] = _screen[r - 1];

        _screen[_scrollTop] = new TerminalCell[_columns];
        for (int c = 0; c < _columns; c++)
            _screen[_scrollTop][c] = TerminalCell.Empty;
    }

    private void SetCell(int row, int col, char ch)
    {
        if (row < 0 || row >= _rows || col < 0 || col >= _columns) return;
        _screen[row][col] = new TerminalCell
        {
            Character = ch,
            Foreground = _currentFg,
            Background = _currentBg,
            Attributes = _currentAttrs
        };
    }

    private void EraseLine(int row)
    {
        if (row < 0 || row >= _rows) return;
        for (int c = 0; c < _columns; c++)
            _screen[row][c] = TerminalCell.Empty;
    }

    private void EraseLineFrom(int row, int fromCol)
    {
        if (row < 0 || row >= _rows) return;
        for (int c = fromCol; c < _columns; c++)
            _screen[row][c] = TerminalCell.Empty;
    }

    private void EraseLineTo(int row, int toCol)
    {
        if (row < 0 || row >= _rows) return;
        for (int c = 0; c <= toCol && c < _columns; c++)
            _screen[row][c] = TerminalCell.Empty;
    }

    private void InsertBlanksAt(int row, int col, int n)
    {
        if (row < 0 || row >= _rows) return;
        for (int c = _columns - 1; c >= col + n; c--)
            _screen[row][c] = _screen[row][c - n];
        for (int c = col; c < Math.Min(col + n, _columns); c++)
            _screen[row][c] = TerminalCell.Empty;
    }

    private static char MapLineDrawing(char ch) => ch switch
    {
        'j' => '┘', 'k' => '┐', 'l' => '┌', 'm' => '└', 'n' => '┼',
        'q' => '─', 't' => '├', 'u' => '┤', 'v' => '┴', 'w' => '┬', 'x' => '│',
        '`' => '◆', 'a' => '▒', 'f' => '°', 'g' => '±', 'h' => '░',
        'i' => '␉', 'o' => '⎺', 'p' => '⎻', 'r' => '⎼', 's' => '⎽',
        _ => ch
    };

    private void NotifyChanged() => BufferChanged?.Invoke(this, EventArgs.Empty);

    public (int row, int col) GetCursorPosition()
    {
        lock (_lock) return (_cursorRow + 1, _cursorCol + 1);
    }
}

/// <summary>Immutable snapshot of the terminal buffer for rendering.</summary>
public sealed class BufferSnapshot
{
    public TerminalLine[] Lines { get; }
    public int Columns { get; }
    public int Rows { get; }
    public int CursorRow { get; }
    public int CursorCol { get; }
    public bool CursorVisible { get; }
    public int ScrollbackCount { get; }
    public int ViewportScroll { get; }

    public BufferSnapshot(TerminalLine[] lines, int cols, int rows, int curRow, int curCol,
        bool curVis, int sbCount, int vpScroll)
    {
        Lines = lines; Columns = cols; Rows = rows;
        CursorRow = curRow; CursorCol = curCol; CursorVisible = curVis;
        ScrollbackCount = sbCount; ViewportScroll = vpScroll;
    }
}

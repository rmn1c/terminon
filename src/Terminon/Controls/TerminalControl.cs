using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Terminon.Terminal;
using Terminon.ViewModels;

namespace Terminon.Controls;

/// <summary>
/// High-performance WPF terminal emulator control.
/// Renders the terminal buffer using DrawingContext for efficiency.
/// </summary>
public class TerminalControl : FrameworkElement
{
    // ─── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(nameof(Session), typeof(SessionTabViewModel), typeof(TerminalControl),
            new PropertyMetadata(null, OnSessionChanged));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(TerminalControl),
            new PropertyMetadata(new FontFamily("Cascadia Code"), OnFontChanged));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TerminalControl),
            new PropertyMetadata(14.0, OnFontChanged));

    public static readonly DependencyProperty ColorSchemeProperty =
        DependencyProperty.Register(nameof(ColorScheme), typeof(ColorScheme), typeof(TerminalControl),
            new PropertyMetadata(ColorScheme.Dark, (d, _) => ((TerminalControl)d).InvalidateVisual()));

    public SessionTabViewModel? Session
    {
        get => (SessionTabViewModel?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public ColorScheme ColorScheme
    {
        get => (ColorScheme)GetValue(ColorSchemeProperty);
        set => SetValue(ColorSchemeProperty, value);
    }

    // ─── Private Fields ────────────────────────────────────────────────────────

    private double _charWidth;
    private double _charHeight;
    private double _charBaseline;

    // Cached typefaces
    private GlyphTypeface? _glyphTypeface;
    private GlyphTypeface? _boldGlyphTypeface;

    // Cursor blink
    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorBlinkOn = true;

    // Selection
    private bool _isSelecting;
    private Point _selectionStart;
    private Point _selectionEnd;
    private (int row, int col) _selStartCell;
    private (int row, int col) _selEndCell;
    private bool _hasSelection;

    // Bell flash
    private bool _bellFlash;
    private readonly DispatcherTimer _bellTimer;

    // Search
    private string _searchTerm = string.Empty;
    private readonly List<(int row, int col)> _searchHits = new();
    private int _searchHitIndex = -1;

    // Scroll offset (in lines, from top of scrollback)
    private int _viewportScrollLines;

    // Pixel dimensions cached
    private int _termCols;
    private int _termRows;

    public static readonly DependencyProperty CursorBlinkRateProperty =
        DependencyProperty.Register(nameof(CursorBlinkRate), typeof(int), typeof(TerminalControl),
            new PropertyMetadata(530, (d, e) => ((TerminalControl)d)._cursorTimer.Interval = TimeSpan.FromMilliseconds((int)e.NewValue)));

    public int CursorBlinkRate
    {
        get => (int)GetValue(CursorBlinkRateProperty);
        set => SetValue(CursorBlinkRateProperty, value);
    }

    public TerminalControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) => { _cursorBlinkOn = !_cursorBlinkOn; InvalidateVisual(); };
        _cursorTimer.Start();

        _bellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _bellTimer.Tick += (_, _) => { _bellFlash = false; _bellTimer.Stop(); InvalidateVisual(); };

        DataContextChanged += (_, _) => UpdateSession();
        SizeChanged += OnSizeChanged;
    }

    private static void OnSessionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalControl tc)
        {
            if (e.OldValue is SessionTabViewModel oldVm)
            {
                oldVm.Buffer.BufferChanged -= tc.OnBufferChanged;
                oldVm.SystemSounds -= tc.TriggerBell;
            }
            if (e.NewValue is SessionTabViewModel newVm)
            {
                newVm.Buffer.BufferChanged += tc.OnBufferChanged;
                newVm.SystemSounds += tc.TriggerBell;
                tc.InvalidateVisual();
            }
        }
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalControl tc)
        {
            tc.RecalculateFontMetrics();
            tc.InvalidateVisual();
        }
    }

    private void UpdateSession()
    {
        if (DataContext is SessionTabViewModel vm)
            Session = vm;
    }

    private void RecalculateFontMetrics()
    {
        _glyphTypeface = null;
        _boldGlyphTypeface = null;

        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (typeface.TryGetGlyphTypeface(out var gt))
            _glyphTypeface = gt;

        var boldTypeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        if (boldTypeface.TryGetGlyphTypeface(out var bgt))
            _boldGlyphTypeface = bgt;

        double dpi = PresentationSource.FromVisual(this) is { } src
            ? 96.0 * src.CompositionTarget.TransformToDevice.M11
            : 96.0;
        double pixelsPerDip = dpi / 96.0;

        // Measure 'W' for cell width (widest monospace char)
        if (_glyphTypeface is not null)
        {
            _glyphTypeface.CharacterToGlyphMap.TryGetValue('W', out var gi);
            _charWidth = _glyphTypeface.AdvanceWidths[gi] * FontSize;
            _charHeight = _glyphTypeface.Height * FontSize;
            _charBaseline = _glyphTypeface.Baseline * FontSize;
        }
        else
        {
            var ft = new FormattedText("W", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily), FontSize, Brushes.White, pixelsPerDip);
            _charWidth = ft.Width;
            _charHeight = ft.Height;
            _charBaseline = ft.Baseline;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var scheme = ColorScheme ?? ColorScheme.Dark;
        var bgBrush = GetBrush(scheme.DefaultBackground);
        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_bellFlash)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), null,
                new Rect(0, 0, ActualWidth, ActualHeight));
        }

        if (Session is null || _charWidth <= 0 || _charHeight <= 0) return;

        RecalculateTermSize();
        var snapshot = Session.Buffer.Snapshot();
        RenderLines(dc, snapshot, scheme);
    }

    private void RenderLines(DrawingContext dc, BufferSnapshot snap, ColorScheme scheme)
    {
        double dpi = PresentationSource.FromVisual(this) is { } src
            ? src.CompositionTarget.TransformToDevice.M11
            : 1.0;

        for (int row = 0; row < snap.Rows && row < snap.Lines.Length; row++)
        {
            double y = row * _charHeight;
            var line = snap.Lines[row];
            if (line is null) continue;

            // Draw backgrounds first (only non-default)
            for (int col = 0; col < snap.Columns; col++)
            {
                var cell = line[col];
                var bg = GetCellBackground(cell, scheme);
                if (bg.A > 0)
                {
                    dc.DrawRectangle(GetBrush(bg), null,
                        new Rect(col * _charWidth, y, _charWidth, _charHeight));
                }
            }

            // Draw selection highlight
            if (_hasSelection)
            {
                var (sr, sc) = NormalizeSelection(_selStartCell, _selEndCell).start;
                var (er, ec) = NormalizeSelection(_selStartCell, _selEndCell).end;
                if (row >= sr && row <= er)
                {
                    int startCol = row == sr ? sc : 0;
                    int endCol = row == er ? ec : snap.Columns - 1;
                    var selBrush = GetBrush(scheme.SelectionBackground);
                    dc.DrawRectangle(selBrush, null,
                        new Rect(startCol * _charWidth, y, (endCol - startCol + 1) * _charWidth, _charHeight));
                }
            }

            // Draw search highlights
            foreach (var (hr, hc) in _searchHits)
            {
                if (hr == row)
                    dc.DrawRectangle(GetBrush(Color.FromArgb(120, 255, 200, 50)), null,
                        new Rect(hc * _charWidth, y, _charWidth, _charHeight));
            }

            // Draw text using FormattedText spans for color accuracy
            RenderLineText(dc, line, snap.Columns, row, y, scheme, dpi);

            // Draw cursor
            if (snap.CursorVisible && snap.CursorRow == row && _cursorBlinkOn)
            {
                double cx = snap.CursorCol * _charWidth;
                dc.DrawRectangle(GetBrush(scheme.CursorColor), null,
                    new Rect(cx, y + _charHeight - 2, _charWidth, 2));
                // Block cursor when focused
                if (IsFocused)
                    dc.DrawRectangle(GetBrush(scheme.CursorColor), null,
                        new Rect(cx, y, _charWidth, _charHeight));
            }
        }
    }

    private void RenderLineText(DrawingContext dc, TerminalLine line, int cols, int row, double y, ColorScheme scheme, double pixelsPerDip)
    {
        if (_glyphTypeface is null)
        {
            // Fallback: FormattedText per line
            var sb = new StringBuilder(cols);
            for (int c = 0; c < cols; c++)
                sb.Append(line[c].Character == '\0' ? ' ' : line[c].Character);
            var ft = new FormattedText(sb.ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily), FontSize,
                GetBrush(scheme.DefaultForeground), pixelsPerDip);
            for (int c = 0; c < cols; c++)
            {
                var cell = line[c];
                var fg = GetCellForeground(cell, scheme);
                ft.SetForegroundBrush(GetBrush(fg), c, 1);
            }
            dc.DrawText(ft, new Point(0, y));
            return;
        }

        // GlyphRun-based rendering per span of same style
        int spanStart = 0;
        while (spanStart < cols)
        {
            var refCell = line[spanStart];
            var fg = GetCellForeground(refCell, scheme);
            bool bold = refCell.HasAttribute(CellAttributes.Bold);
            bool italic = refCell.HasAttribute(CellAttributes.Italic);

            int spanEnd = spanStart + 1;
            while (spanEnd < cols)
            {
                var c = line[spanEnd];
                var cfg = GetCellForeground(c, scheme);
                if (cfg != fg || c.HasAttribute(CellAttributes.Bold) != bold) break;
                spanEnd++;
            }

            // Build glyph run for [spanStart, spanEnd)
            var gt = bold && _boldGlyphTypeface is not null ? _boldGlyphTypeface : _glyphTypeface;
            var glyphs = new List<ushort>(spanEnd - spanStart);
            var advances = new List<double>(spanEnd - spanStart);

            for (int i = spanStart; i < spanEnd; i++)
            {
                char ch = line[i].Character;
                if (ch == '\0') ch = ' ';
                gt.CharacterToGlyphMap.TryGetValue(ch, out var glyphIdx);
                glyphs.Add(glyphIdx);
                advances.Add(_charWidth);
            }

            if (glyphs.Count > 0)
            {
                try
                {
                    var gr = new GlyphRun(
                        glyphTypeface: gt,
                        bidiLevel: 0,
                        isSideways: false,
                        renderingEmSize: FontSize,
                        pixelsPerDip: (float)pixelsPerDip,
                        glyphIndices: glyphs,
                        baselineOrigin: new Point(spanStart * _charWidth, y + _charBaseline),
                        advanceWidths: advances,
                        glyphOffsets: null,
                        characters: null,
                        deviceFontName: null,
                        clusterMap: null,
                        caretStops: null,
                        language: null);
                    dc.DrawGlyphRun(GetBrush(fg), gr);
                }
                catch { /* skip span on render error */ }
            }

            // Decorations
            if (refCell.HasAttribute(CellAttributes.Underline))
            {
                dc.DrawLine(new Pen(GetBrush(fg), 1),
                    new Point(spanStart * _charWidth, y + _charHeight - 2),
                    new Point(spanEnd * _charWidth, y + _charHeight - 2));
            }
            if (refCell.HasAttribute(CellAttributes.Strikethrough))
            {
                dc.DrawLine(new Pen(GetBrush(fg), 1),
                    new Point(spanStart * _charWidth, y + _charHeight / 2),
                    new Point(spanEnd * _charWidth, y + _charHeight / 2));
            }

            spanStart = spanEnd;
        }
    }

    private Color GetCellForeground(TerminalCell cell, ColorScheme scheme)
    {
        bool reverse = cell.HasAttribute(CellAttributes.Reverse);
        bool invisible = cell.HasAttribute(CellAttributes.Invisible);
        if (invisible) return GetCellBackground(cell, scheme);

        var fg = reverse
            ? (cell.Background.IsDefault ? scheme.DefaultBackground : cell.Background.Resolve(false, false, scheme))
            : cell.Foreground.Resolve(true, cell.HasAttribute(CellAttributes.Bold), scheme);

        if (cell.HasAttribute(CellAttributes.Dim))
            fg = Color.FromArgb((byte)(fg.A * 0.6), fg.R, fg.G, fg.B);

        return fg;
    }

    private Color GetCellBackground(TerminalCell cell, ColorScheme scheme)
    {
        bool reverse = cell.HasAttribute(CellAttributes.Reverse);
        if (reverse)
            return cell.Foreground.IsDefault ? scheme.DefaultForeground : cell.Foreground.Resolve(true, false, scheme);
        if (cell.Background.IsDefault) return Colors.Transparent;
        return cell.Background.Resolve(false, false, scheme);
    }

    // ─── Input Handling ────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Session is null || !Session.IsConnected) return;

        var bytes = TranslateKey(e);
        if (bytes is not null)
        {
            _ = Session.SendDataAsync(bytes);
            e.Handled = true;
            // Scroll to bottom on input
            Session.Buffer.ViewportScroll = 0;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (Session is null || !Session.IsConnected || string.IsNullOrEmpty(e.Text)) return;
        var bytes = Encoding.UTF8.GetBytes(e.Text);
        _ = Session.SendDataAsync(bytes);
        Session.Buffer.ViewportScroll = 0;
        e.Handled = true;
    }

    private byte[]? TranslateKey(KeyEventArgs e)
    {
        bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (e.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0;

        // Copy / Paste
        if (ctrl && e.Key == Key.C && _hasSelection) { CopySelection(); return null; }
        if (ctrl && e.Key == Key.V) { PasteFromClipboard(); return null; }
        if (ctrl && e.Key == Key.F) { Session?.ToggleSearchCommand.Execute(null); return null; }

        // Scrollback
        if (shift && e.Key == Key.PageUp) { ScrollViewport(-Session!.Buffer.Rows); return null; }
        if (shift && e.Key == Key.PageDown) { ScrollViewport(Session!.Buffer.Rows); return null; }

        return e.Key switch
        {
            Key.Enter => "\r"u8.ToArray(),
            Key.Escape => "\x1b"u8.ToArray(),
            Key.Back => "\x7F"u8.ToArray(),
            Key.Tab => "\t"u8.ToArray(),
            Key.Delete => "\x1b[3~"u8.ToArray(),
            Key.Home => ctrl ? "\x1b[1~"u8.ToArray() : "\x1b[H"u8.ToArray(),
            Key.End => ctrl ? "\x1b[4~"u8.ToArray() : "\x1b[F"u8.ToArray(),
            Key.PageUp => "\x1b[5~"u8.ToArray(),
            Key.PageDown => "\x1b[6~"u8.ToArray(),
            Key.Up => ctrl ? "\x1b[1;5A"u8.ToArray() : "\x1b[A"u8.ToArray(),
            Key.Down => ctrl ? "\x1b[1;5B"u8.ToArray() : "\x1b[B"u8.ToArray(),
            Key.Right => ctrl ? "\x1b[1;5C"u8.ToArray() : "\x1b[C"u8.ToArray(),
            Key.Left => ctrl ? "\x1b[1;5D"u8.ToArray() : "\x1b[D"u8.ToArray(),
            Key.Insert => "\x1b[2~"u8.ToArray(),
            Key.F1 => "\x1bOP"u8.ToArray(),
            Key.F2 => "\x1bOQ"u8.ToArray(),
            Key.F3 => "\x1bOR"u8.ToArray(),
            Key.F4 => "\x1bOS"u8.ToArray(),
            Key.F5 => "\x1b[15~"u8.ToArray(),
            Key.F6 => "\x1b[17~"u8.ToArray(),
            Key.F7 => "\x1b[18~"u8.ToArray(),
            Key.F8 => "\x1b[19~"u8.ToArray(),
            Key.F9 => "\x1b[20~"u8.ToArray(),
            Key.F10 => "\x1b[21~"u8.ToArray(),
            Key.F11 => "\x1b[23~"u8.ToArray(),
            Key.F12 => "\x1b[24~"u8.ToArray(),
            Key k when ctrl && k >= Key.A && k <= Key.Z =>
                new[] { (byte)(k - Key.A + 1) },
            _ => null
        };
    }

    // ─── Mouse Handling ────────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.ChangedButton == MouseButton.Left)
        {
            _isSelecting = true;
            _hasSelection = false;
            _selectionStart = e.GetPosition(this);
            _selectionEnd = _selectionStart;
            _selStartCell = PointToCell(_selectionStart);
            _selEndCell = _selStartCell;
            CaptureMouse();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            if (_hasSelection) CopySelection();
            else PasteFromClipboard();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isSelecting)
        {
            _selectionEnd = e.GetPosition(this);
            _selEndCell = PointToCell(_selectionEnd);
            _hasSelection = _selStartCell != _selEndCell;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Left && _isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int lines = e.Delta > 0 ? -3 : 3;
        ScrollViewport(lines);
        e.Handled = true;
    }

    private void ScrollViewport(int lines)
    {
        if (Session is null) return;
        var buf = Session.Buffer;
        buf.ViewportScroll = Math.Clamp(buf.ViewportScroll - lines, 0, buf.ScrollbackCount);
        InvalidateVisual();
    }

    // ─── Copy / Paste ──────────────────────────────────────────────────────────

    private void CopySelection()
    {
        if (!_hasSelection || Session is null) return;
        var snap = Session.Buffer.Snapshot();
        var (start, end) = NormalizeSelection(_selStartCell, _selEndCell);
        var sb = new StringBuilder();
        for (int r = start.row; r <= end.row && r < snap.Lines.Length; r++)
        {
            var line = snap.Lines[r];
            int cs = r == start.row ? start.col : 0;
            int ce = r == end.row ? end.col : snap.Columns - 1;
            for (int c = cs; c <= ce && c < snap.Columns; c++)
                sb.Append(line[c].Character == '\0' ? ' ' : line[c].Character);
            if (r < end.row) sb.AppendLine();
        }
        Clipboard.SetText(sb.ToString().TrimEnd());
    }

    private void PasteFromClipboard()
    {
        if (Session is null || !Session.IsConnected) return;
        var text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;
        _ = Session.SendDataAsync(Encoding.UTF8.GetBytes(text));
    }

    // ─── Search ────────────────────────────────────────────────────────────────

    public void Search(string term)
    {
        _searchTerm = term;
        _searchHits.Clear();
        _searchHitIndex = -1;

        if (string.IsNullOrEmpty(term) || Session is null) { InvalidateVisual(); return; }

        var snap = Session.Buffer.Snapshot();
        for (int r = 0; r < snap.Lines.Length; r++)
        {
            var line = snap.Lines[r];
            var sb = new StringBuilder(snap.Columns);
            for (int c = 0; c < snap.Columns; c++)
                sb.Append(line[c].Character == '\0' ? ' ' : line[c].Character);
            string lineStr = sb.ToString();
            int idx = 0;
            while ((idx = lineStr.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                _searchHits.Add((r, idx));
                idx += term.Length;
            }
        }

        if (_searchHits.Count > 0)
        {
            _searchHitIndex = 0;
            ScrollToSearchHit();
        }
        InvalidateVisual();
    }

    public void SearchNext()
    {
        if (_searchHits.Count == 0) return;
        _searchHitIndex = (_searchHitIndex + 1) % _searchHits.Count;
        ScrollToSearchHit();
        InvalidateVisual();
    }

    public void SearchPrev()
    {
        if (_searchHits.Count == 0) return;
        _searchHitIndex = (_searchHitIndex - 1 + _searchHits.Count) % _searchHits.Count;
        ScrollToSearchHit();
        InvalidateVisual();
    }

    private void ScrollToSearchHit()
    {
        if (_searchHitIndex < 0 || _searchHitIndex >= _searchHits.Count) return;
        // TODO: adjust viewport scroll to show the hit
    }

    // ─── Layout ────────────────────────────────────────────────────────────────

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateTermSize();
        if (Session is not null)
            _ = Session.ResizeTerminalAsync(_termCols, _termRows);
    }

    private void RecalculateTermSize()
    {
        if (_charWidth <= 0 || _charHeight <= 0) return;
        int cols = Math.Max(1, (int)(ActualWidth / _charWidth));
        int rows = Math.Max(1, (int)(ActualHeight / _charHeight));
        if (cols != _termCols || rows != _termRows)
        {
            _termCols = cols;
            _termRows = rows;
        }
    }

    private (int row, int col) PointToCell(Point p)
    {
        if (_charWidth <= 0 || _charHeight <= 0) return (0, 0);
        int col = Math.Clamp((int)(p.X / _charWidth), 0, Math.Max(_termCols - 1, 0));
        int row = Math.Clamp((int)(p.Y / _charHeight), 0, Math.Max(_termRows - 1, 0));
        return (row, col);
    }

    private static ((int row, int col) start, (int row, int col) end) NormalizeSelection(
        (int row, int col) a, (int row, int col) b)
    {
        if (a.row < b.row || (a.row == b.row && a.col <= b.col))
            return (a, b);
        return (b, a);
    }

    private void OnBufferChanged(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Render);

    private void TriggerBell()
    {
        _bellFlash = true;
        _bellTimer.Stop();
        _bellTimer.Start();
        InvalidateVisual();
    }

    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);
        _cursorTimer.Start();
        InvalidateVisual();
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _cursorBlinkOn = true;
        InvalidateVisual();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        // Defer font metrics until layout is ready so DPI is available
        Dispatcher.InvokeAsync(RecalculateFontMetrics, DispatcherPriority.Loaded);
    }

    // ─── Brush cache ──────────────────────────────────────────────────────────

    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = new();
    private static SolidColorBrush GetBrush(Color c)
    {
        if (!_brushCache.TryGetValue(c, out var b))
        {
            b = new SolidColorBrush(c);
            b.Freeze();
            _brushCache[c] = b;
        }
        return b;
    }
}

using System.Text;

namespace Terminon.Terminal;

/// <summary>
/// Parses VT100/VT220/xterm byte streams and drives a TerminalBuffer.
/// Implements the DEC ANSI parser state machine.
/// </summary>
public sealed class VT100Parser
{
    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        DcsEntry,
        DcsPassthrough,
        DcsIgnore,
    }

    private readonly TerminalBuffer _buffer;
    private State _state = State.Ground;

    private readonly StringBuilder _oscBuffer = new(256);
    private readonly StringBuilder _paramBuffer = new(64);
    private char _csiIntermediate;
    private char _escIntermediate;

    // UTF-8 decoder (SSH streams are UTF-8)
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charBuf = new char[4];
    private readonly byte[] _byteBuf = new byte[1];

    // Bracketed paste mode
    private bool _bracketedPaste;

    public VT100Parser(TerminalBuffer buffer)
    {
        _buffer = buffer;
    }

    public void Feed(byte[] data, int offset, int count)
    {
        for (int i = offset; i < offset + count; i++)
            FeedByte(data[i]);
    }

    public void Feed(byte[] data) => Feed(data, 0, data.Length);

    private void FeedByte(byte b)
    {
        // Handle UTF-8 multi-byte sequences in Ground state
        if (_state == State.Ground && b >= 0x80)
        {
            _byteBuf[0] = b;
                int charCount = _decoder.GetChars(_byteBuf, 0, 1, _charBuf, 0);
            for (int i = 0; i < charCount; i++)
                _buffer.WriteChar(_charBuf[i]);
            return;
        }

        // C1 control codes (0x80-0x9F) — treat as ESC + (b - 0x40)
        if (b >= 0x80 && b <= 0x9F)
        {
            ProcessEscapeByte((char)(b - 0x40));
            return;
        }

        // Anywhere transitions
        switch (b)
        {
            case 0x18 or 0x1A:  // CAN / SUB — cancel sequence
                _state = State.Ground;
                return;
            case 0x1B:
                _decoder.Reset();
                _state = State.Escape;
                _escIntermediate = '\0';
                return;
        }

        switch (_state)
        {
            case State.Ground:
                ProcessGroundByte(b);
                break;
            case State.Escape:
                ProcessEscapeByte((char)b);
                break;
            case State.EscapeIntermediate:
                if (b >= 0x20 && b <= 0x2F) _escIntermediate = (char)b;
                else if (b >= 0x30 && b <= 0x7E) { DispatchEscape((char)b); _state = State.Ground; }
                else _state = State.Ground;
                break;
            case State.CsiEntry:
                // _paramBuffer and _csiIntermediate already cleared on ESC[
                if (b >= 0x30 && b <= 0x39) { _paramBuffer.Append((char)b); _state = State.CsiParam; }    // digits
                else if (b == 0x3B) { _paramBuffer.Append(';'); _state = State.CsiParam; }                // semicolon
                else if (b == 0x3C || b == 0x3D || b == 0x3E || b == 0x3F)                               // < = > ? (DEC private)
                { _paramBuffer.Append((char)b); _state = State.CsiParam; }
                else if (b >= 0x20 && b <= 0x2F) { _csiIntermediate = (char)b; _state = State.CsiIntermediate; }
                else if (b >= 0x40 && b <= 0x7E) { DispatchCsi('\0', '\0', (char)b); _state = State.Ground; }
                else if (b < 0x20) ProcessGroundByte(b); // execute C0 controls
                break;
            case State.CsiParam:
                if (b >= 0x30 && b <= 0x3F) _paramBuffer.Append((char)b);
                else if (b >= 0x20 && b <= 0x2F) { _csiIntermediate = (char)b; _state = State.CsiIntermediate; }
                else if (b >= 0x40 && b <= 0x7E) { DispatchCsi(_paramBuffer.Length > 0 ? _paramBuffer[0] : '\0', _csiIntermediate, (char)b); _state = State.Ground; }
                else if (b == 0x3B) _paramBuffer.Append(';');
                else _state = State.CsiIgnore;
                break;
            case State.CsiIntermediate:
                if (b >= 0x20 && b <= 0x2F) _csiIntermediate = (char)b;
                else if (b >= 0x40 && b <= 0x7E) { DispatchCsi('\0', _csiIntermediate, (char)b); _state = State.Ground; }
                else _state = State.CsiIgnore;
                break;
            case State.CsiIgnore:
                if (b >= 0x40 && b <= 0x7E) _state = State.Ground;
                break;
            case State.OscString:
                if (b == 0x07 || (b == 0x9C)) { DispatchOsc(_oscBuffer.ToString()); _state = State.Ground; }
                else if (b == 0x1B) { /* next char should be '\\' */ }
                else if (b == 0x5C && _oscBuffer.Length > 0) { DispatchOsc(_oscBuffer.ToString()); _state = State.Ground; }
                else _oscBuffer.Append((char)b);
                break;
            case State.DcsEntry or State.DcsPassthrough or State.DcsIgnore:
                if (b == 0x9C || (b == 0x07)) _state = State.Ground;
                break;
        }
    }

    private void ProcessGroundByte(byte b)
    {
        switch (b)
        {
            case 0x00: break; // NUL
            case 0x07: _buffer.Bell(); break;
            case 0x08: _buffer.Backspace(); break;
            case 0x09: _buffer.Tab(); break;
            case 0x0A or 0x0B or 0x0C: _buffer.LineFeed(); break;
            case 0x0D: _buffer.CarriageReturn(); break;
            case 0x0E: _buffer.SetLineDrawing(true); break;   // SO - G1 charset
            case 0x0F: _buffer.SetLineDrawing(false); break;  // SI - G0 charset
            default:
                if (b >= 0x20 && b <= 0x7E)
                {
                    _buffer.WriteChar((char)b);
                    _decoder.Reset();
                }
                else if (b >= 0x80)
                {
                    _byteBuf[0] = b;
                int charCount = _decoder.GetChars(_byteBuf, 0, 1, _charBuf, 0);
                    for (int i = 0; i < charCount; i++)
                        _buffer.WriteChar(_charBuf[i]);
                }
                break;
        }
    }

    private void ProcessEscapeByte(char c)
    {
        switch (c)
        {
            case '[': _paramBuffer.Clear(); _csiIntermediate = '\0'; _state = State.CsiEntry; break;
            case ']': _state = State.OscString; _oscBuffer.Clear(); break;
            case 'P': _state = State.DcsEntry; break;
            case 'X' or '^' or '_': _state = State.DcsIgnore; break;
            case '7': _buffer.SaveCursor(); _state = State.Ground; break;
            case '8': _buffer.RestoreCursor(); _state = State.Ground; break;
            case 'D': _buffer.LineFeed(); _state = State.Ground; break;
            case 'E': _buffer.CarriageReturn(); _buffer.LineFeed(); _state = State.Ground; break;
            case 'M': _buffer.ScrollDown(1); _state = State.Ground; break; // Reverse index
            case 'c': _buffer.EraseInDisplay(2); _state = State.Ground; break; // RIS
            case '(': _escIntermediate = '('; _state = State.EscapeIntermediate; break;
            case ')': _escIntermediate = ')'; _state = State.EscapeIntermediate; break;
            case '=': _state = State.Ground; break; // DECKPAM
            case '>': _state = State.Ground; break; // DECKPNM
            default:
                if (c >= 0x20 && c <= 0x2F) { _escIntermediate = c; _state = State.EscapeIntermediate; }
                else _state = State.Ground;
                break;
        }
    }

    private void DispatchEscape(char finalByte)
    {
        // ESC intermediate finalByte
        if (_escIntermediate == '(' || _escIntermediate == ')')
        {
            // Character set designation
            if (finalByte == '0') _buffer.SetLineDrawing(true);
            else if (finalByte == 'B') _buffer.SetLineDrawing(false);
        }
    }

    private void DispatchCsi(char prefix, char intermediate, char final)
    {
        string rawParams = _paramBuffer.ToString();

        // Detect DEC private prefix (?, <, >, =) which comes first in param string
        char detectedPrefix = prefix;
        if (detectedPrefix == '\0' && rawParams.Length > 0 && (rawParams[0] == '?' || rawParams[0] == '<' || rawParams[0] == '>' || rawParams[0] == '='))
        {
            detectedPrefix = rawParams[0];
            rawParams = rawParams.Length > 1 ? rawParams[1..] : string.Empty;
        }

        var ps = ParseParams(rawParams);

        // Handle DEC private mode sequences ESC[? ...
        if (detectedPrefix == '?')
        {
            int p = ps.Count > 0 ? ps[0] : 0;
            switch (final)
            {
                case 'h': SetDecMode(p, true); break;
                case 'l': SetDecMode(p, false); break;
            }
            return;
        }

        int p1 = ps.Count > 0 ? ps[0] : 0;
        int p2 = ps.Count > 1 ? ps[1] : 0;

        switch (final)
        {
            case 'A': _buffer.CursorUp(Math.Max(1, p1)); break;
            case 'B': _buffer.CursorDown(Math.Max(1, p1)); break;
            case 'C': _buffer.CursorForward(Math.Max(1, p1)); break;
            case 'D': _buffer.CursorBack(Math.Max(1, p1)); break;
            case 'E': _buffer.CursorNextLine(Math.Max(1, p1)); break;
            case 'F': _buffer.CursorPrevLine(Math.Max(1, p1)); break;
            case 'G': _buffer.CursorHorizontalAbsolute(Math.Max(1, p1)); break;
            case 'H' or 'f': _buffer.CursorPosition(Math.Max(1, p1), Math.Max(1, p2)); break;
            case 'I': for (int i = 0; i < Math.Max(1, p1); i++) _buffer.Tab(); break;
            case 'J': _buffer.EraseInDisplay(p1); break;
            case 'K': _buffer.EraseInLine(p1); break;
            case 'L': _buffer.InsertLines(Math.Max(1, p1)); break;
            case 'M': _buffer.DeleteLines(Math.Max(1, p1)); break;
            case 'P': _buffer.DeleteCharacters(Math.Max(1, p1)); break;
            case 'S': _buffer.ScrollUp(Math.Max(1, p1)); break;
            case 'T': _buffer.ScrollDown(Math.Max(1, p1)); break;
            case 'X': _buffer.EraseCharacters(Math.Max(1, p1)); break;
            case '@': _buffer.InsertBlanks(Math.Max(1, p1)); break;
            case 'm': _buffer.SetGraphicsRendition(ps); break;
            case 'r': _buffer.SetScrollRegion(Math.Max(1, p1), p2 == 0 ? _buffer.Rows : p2); break;
            case 's': _buffer.SaveCursor(); break;
            case 'u': _buffer.RestoreCursor(); break;
            case 'n': HandleDeviceStatusReport(p1); break;
            case 'c': /* Device Attributes - respond with VT220 */ break;
            case 'd': _buffer.CursorPosition(Math.Max(1, p1), _buffer.GetCursorPosition().col); break;  // VPA — vertical line position absolute
            case 'h': if (p1 == 4) _buffer.SetInsertMode(true); break;
            case 'l': if (p1 == 4) _buffer.SetInsertMode(false); break;
        }
    }

    private void SetDecMode(int mode, bool enable)
    {
        switch (mode)
        {
            case 1: /* Application cursor keys */ break;
            case 3: /* 132 column mode */ break;
            case 6: _buffer.SetOriginMode(enable); break;
            case 7: _buffer.SetAutoWrap(enable); break;
            case 12: /* Cursor blink */ break;
            case 25: _buffer.ShowCursor(enable); break;
            case 47 or 1047: if (enable) _buffer.SwitchToAltScreen(); else _buffer.SwitchToNormalScreen(); break;
            case 1048: if (enable) _buffer.SaveCursor(); else _buffer.RestoreCursor(); break;
            case 1049:
                if (enable) { _buffer.SaveCursor(); _buffer.SwitchToAltScreen(); _buffer.EraseInDisplay(2); }
                else { _buffer.SwitchToNormalScreen(); _buffer.RestoreCursor(); }
                break;
            case 2004: _bracketedPaste = enable; break;
        }
    }

    private void HandleDeviceStatusReport(int p)
    {
        // Would need a write-back channel — skip for now
    }

    private void DispatchOsc(string data)
    {
        // OSC sequences: "N;...text..."
        int semi = data.IndexOf(';');
        if (semi < 0) return;
        if (!int.TryParse(data.AsSpan(0, semi), out int code)) return;
        string arg = data[(semi + 1)..];
        switch (code)
        {
            case 0 or 2: _buffer.SetTitle(arg); break; // Set window/icon title
            case 1: break; // Icon name
        }
    }

    private static List<int> ParseParams(string raw)
    {
        var result = new List<int>(8);
        if (string.IsNullOrEmpty(raw)) return result;
        foreach (var part in raw.Split(';'))
        {
            if (int.TryParse(part, out int v)) result.Add(v);
            else result.Add(0);
        }
        return result;
    }

    public bool BracketedPasteMode => _bracketedPaste;
}

using System.Windows.Media;

namespace Terminon.Terminal;

public enum TerminalColorType { Default, Indexed, RGB }

public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    public TerminalColorType Type { get; }
    public int Index { get; }   // 0-255 for Indexed
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    private TerminalColor(TerminalColorType type, int index, byte r, byte g, byte b)
    {
        Type = type; Index = index; R = r; G = g; B = b;
    }

    public static TerminalColor Default => new(TerminalColorType.Default, 0, 0, 0, 0);
    public static TerminalColor FromIndex(int index) => new(TerminalColorType.Indexed, index, 0, 0, 0);
    public static TerminalColor FromRGB(byte r, byte g, byte b) => new(TerminalColorType.RGB, 0, r, g, b);

    public bool IsDefault => Type == TerminalColorType.Default;

    public Color Resolve(bool isForeground, bool isBold, ColorScheme scheme)
    {
        return Type switch
        {
            TerminalColorType.Default => isForeground ? scheme.DefaultForeground : scheme.DefaultBackground,
            TerminalColorType.Indexed => ResolveIndexed(Index, isBold && isForeground && Index < 8, scheme),
            TerminalColorType.RGB => Color.FromRgb(R, G, B),
            _ => Colors.Transparent
        };
    }

    private static Color ResolveIndexed(int index, bool bright, ColorScheme scheme)
    {
        // Standard 16 colors
        if (index < 16)
        {
            int effectiveIndex = bright && index < 8 ? index + 8 : index;
            return scheme.AnsiColors[effectiveIndex];
        }
        // 216-color cube (16-231)
        if (index < 232)
        {
            int i = index - 16;
            int b = i % 6;
            int g = (i / 6) % 6;
            int r = i / 36;
            static byte To255(int v) => v == 0 ? (byte)0 : (byte)(55 + v * 40);
            return Color.FromRgb(To255(r), To255(g), To255(b));
        }
        // Grayscale (232-255)
        {
            int gray = (index - 232) * 10 + 8;
            return Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
        }
    }

    public bool Equals(TerminalColor other) =>
        Type == other.Type && Index == other.Index && R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is TerminalColor c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(Type, Index, R, G, B);
    public static bool operator ==(TerminalColor a, TerminalColor b) => a.Equals(b);
    public static bool operator !=(TerminalColor a, TerminalColor b) => !a.Equals(b);
}

public class ColorScheme
{
    public Color DefaultForeground { get; set; } = Color.FromRgb(204, 204, 204);
    public Color DefaultBackground { get; set; } = Color.FromRgb(18, 18, 18);
    public Color CursorColor { get; set; } = Color.FromRgb(204, 204, 204);
    public Color SelectionBackground { get; set; } = Color.FromArgb(100, 51, 102, 187);

    /// <summary>ANSI colors: 0-7 normal, 8-15 bright.</summary>
    public Color[] AnsiColors { get; set; } = new Color[16]
    {
        // Normal
        Color.FromRgb(0, 0, 0),         // 0 Black
        Color.FromRgb(170, 0, 0),       // 1 Red
        Color.FromRgb(0, 170, 0),       // 2 Green
        Color.FromRgb(170, 85, 0),      // 3 Yellow (Brown)
        Color.FromRgb(0, 0, 170),       // 4 Blue
        Color.FromRgb(170, 0, 170),     // 5 Magenta
        Color.FromRgb(0, 170, 170),     // 6 Cyan
        Color.FromRgb(170, 170, 170),   // 7 White
        // Bright
        Color.FromRgb(85, 85, 85),      // 8 Bright Black
        Color.FromRgb(255, 85, 85),     // 9 Bright Red
        Color.FromRgb(85, 255, 85),     // 10 Bright Green
        Color.FromRgb(255, 255, 85),    // 11 Bright Yellow
        Color.FromRgb(85, 85, 255),     // 12 Bright Blue
        Color.FromRgb(255, 85, 255),    // 13 Bright Magenta
        Color.FromRgb(85, 255, 255),    // 14 Bright Cyan
        Color.FromRgb(255, 255, 255),   // 15 Bright White
    };

    public static ColorScheme Dark => new();

    public static ColorScheme Light => new()
    {
        DefaultForeground = Color.FromRgb(32, 32, 32),
        DefaultBackground = Color.FromRgb(255, 255, 255),
        CursorColor = Color.FromRgb(32, 32, 32),
        SelectionBackground = Color.FromArgb(100, 51, 102, 187),
        AnsiColors = new Color[16]
        {
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(187, 0, 0),
            Color.FromRgb(0, 187, 0),
            Color.FromRgb(187, 187, 0),
            Color.FromRgb(0, 0, 187),
            Color.FromRgb(187, 0, 187),
            Color.FromRgb(0, 187, 187),
            Color.FromRgb(187, 187, 187),
            Color.FromRgb(85, 85, 85),
            Color.FromRgb(255, 85, 85),
            Color.FromRgb(85, 255, 85),
            Color.FromRgb(255, 255, 85),
            Color.FromRgb(85, 85, 255),
            Color.FromRgb(255, 85, 255),
            Color.FromRgb(85, 255, 255),
            Color.FromRgb(255, 255, 255),
        }
    };
}

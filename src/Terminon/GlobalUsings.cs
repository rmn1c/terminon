// Resolve type ambiguities that arise from having both UseWPF and UseWindowsForms enabled.
// System.Windows.Forms and System.Drawing pollute the global namespace with types that clash
// with WPF types. These aliases pin each ambiguous name to its WPF / BCL counterpart;
// Forms/Drawing types must be referenced by their full name where actually needed.

// ── System.IO ─────────────────────────────────────────────────────────────────────────────
// WPF implicit usings do not include System.IO, so Path/File/Directory/FileInfo are missing.
global using System.IO;

// ── System.Windows.Media ──────────────────────────────────────────────────────────────────
global using Brush      = System.Windows.Media.Brush;
global using Brushes    = System.Windows.Media.Brushes;
global using Color      = System.Windows.Media.Color;
global using FontFamily = System.Windows.Media.FontFamily;
global using Pen        = System.Windows.Media.Pen;

// ── System.Windows / System.Windows.Input ─────────────────────────────────────────────────
global using Application    = System.Windows.Application;
global using Clipboard      = System.Windows.Clipboard;
global using Cursors        = System.Windows.Input.Cursors;
global using KeyEventArgs   = System.Windows.Input.KeyEventArgs;
global using MessageBox     = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Point          = System.Windows.Point;
global using UserControl    = System.Windows.Controls.UserControl;

// ── System.Windows.Data ───────────────────────────────────────────────────────────────────
global using Binding = System.Windows.Data.Binding;

// ── Microsoft.Win32 ───────────────────────────────────────────────────────────────────────
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

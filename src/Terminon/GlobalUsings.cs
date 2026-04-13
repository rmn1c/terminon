// Resolve type ambiguities that arise from having both UseWPF and UseWindowsForms enabled.
// System.Windows.Forms pollutes the global namespace with types that clash with WPF / System.Windows.Media.
// These aliases pin each ambiguous name to its WPF counterpart; Forms types must be referenced by full name.

global using Application    = System.Windows.Application;
global using Color          = System.Windows.Media.Color;
global using FontFamily     = System.Windows.Media.FontFamily;
global using KeyEventArgs   = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Point          = System.Windows.Point;
global using UserControl    = System.Windows.Controls.UserControl;
global using Stream         = System.IO.Stream;

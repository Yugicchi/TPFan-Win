// Global using directives for TPFan.GUI — disambiguates types that exist in both
// System.Drawing (WinForms) and System.Windows.Media (WPF).
// Types used only by one framework are NOT global-aliased to avoid breaking that
// framework's own types (e.g. Application must stay WinForms for SystemTrayManager).

global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using Point = System.Windows.Point;

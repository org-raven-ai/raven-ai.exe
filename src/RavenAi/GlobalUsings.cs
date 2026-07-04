// Because UseWindowsForms is enabled (solely for the tray NotifyIcon), both System.Windows
// and System.Windows.Forms are implicitly imported, which makes several common type names
// ambiguous. This app is WPF-first, so alias the ambiguous names to their WPF types globally.
// Code that needs the WinForms equivalents uses the `Forms.` alias explicitly.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Color = System.Windows.Media.Color;
global using SolidColorBrush = System.Windows.Media.SolidColorBrush;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;

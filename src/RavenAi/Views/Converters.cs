using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace RavenAi.Views;

/// <summary>Aligns a chat bubble: user messages to the right, assistant to the left.</summary>
public sealed class BoolToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Picks a bubble background: accent for the user, muted for the assistant.</summary>
public sealed class BoolToBubbleBrushConverter : IValueConverter
{
    private static readonly Brush UserBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
    private static readonly Brush AssistantBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? UserBrush : AssistantBrush;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>bool -> Visibility, with "Invert" parameter to reverse.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase)) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Non-empty string -> Visible, empty -> Collapsed. For inline error banners.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

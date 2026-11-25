using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Unpark.Converters;

/// <summary>true → masqué, false → visible (ex. bouton « Activer » seulement si c'est désactivé).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

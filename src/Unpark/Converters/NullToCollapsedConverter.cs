using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Unpark.Converters;

/// <summary>Visible si la valeur n'est pas nulle (ni vide), sinon masqué.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIHelperNET.App;

/// <summary>Converts a boolean to the inverse Visibility (true → Collapsed, false → Visible).</summary>
public sealed class BoolToVisibilityInverseConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a boolean to one of two color strings supplied as "trueColor|falseColor" in the parameter.</summary>
public sealed class BoolToColorConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? ["#FFFFFF", "#444444"];
        var hex = value is true ? parts[0] : parts[1];
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(color);
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a boolean to one of two strings supplied as "trueString|falseString" in the parameter.</summary>
public sealed class BoolToStringConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string)?.Split('|') ?? ["True", "False"];
        return value is true ? parts[0] : parts[1];
    }

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a boolean to a width value. True → parameter value, False → 0.</summary>
public sealed class BoolToWidthConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? double.Parse(parameter as string ?? "120", CultureInfo.InvariantCulture) : 0.0;

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts an enum value to bool by comparing with the parameter string.</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    /// <inheritdoc/>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true) return Binding.DoNothing;
        return Enum.Parse(targetType, parameter?.ToString() ?? string.Empty);
    }
}

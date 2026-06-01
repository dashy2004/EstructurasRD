using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LosasPlus;

/// <summary>
/// Port a Avalonia: los converters originales devolvían System.Windows.Visibility;
/// en Avalonia se enlazan a <c>IsVisible</c> (bool). Se mantienen los nombres para
/// no tocar las claves de recurso, pero ahora devuelven bool.
/// </summary>

/// <summary>true (visible) si el valor NO es null ni string vacío.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return false;
        if (value is string s && string.IsNullOrEmpty(s)) return false;
        return true;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Oculta (false) cuando el bool es true.</summary>
public sealed class BoolToCollapsedIfTrueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !(value is bool b && b);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Muestra (true) cuando el bool es true.</summary>
public sealed class BoolToCollapsedIfFalseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

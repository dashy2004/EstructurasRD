using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

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

/// <summary>
/// Convierte bytes PNG (<c>byte[]</c>) en un <see cref="Bitmap"/> de Avalonia para
/// enlazar a <c>Image.Source</c>. Es el puente del workaround del bug de render de
/// oxy:PlotView: el ViewModel exporta el diagrama a PNG (vía <c>DiagramaPng</c>) y la
/// vista lo muestra como imagen. Devuelve <see langword="null"/> si no hay bytes.
/// </summary>
public sealed class BytesToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        return null;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LosasPlus.Converters;

/// <summary>
/// Convierte un <see cref="double"/> a string para binding XAML
/// detectando los casos especiales no-finitos: <see cref="double.NaN"/>
/// y ±<see cref="double.PositiveInfinity"/> se renderizan como
/// <c>"—"</c> (em-dash), valores finitos se formatean con tres
/// decimales en cultura invariante (<c>0.847</c>).
///
/// <para>
/// Liga B paso B4 — el panel "Elemento Activo" lo usa para mostrar
/// <c>RatioDC</c> sin exponer al usuario los strings técnicos
/// <c>"NaN"</c> / <c>"Infinity"</c> que produciría el binding por
/// defecto, en aquellos casos en que el calculator no pudo computar
/// el ratio (entidad sin demandas, singularidad numérica, etc.).
/// </para>
/// </summary>
[ValueConversion(typeof(double), typeof(string))]
public sealed class NaNToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "—";
            return d.ToString("F3", CultureInfo.InvariantCulture);
        }
        return "—";
    }

    public object ConvertBack(object value, Type targetType,
        object parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}

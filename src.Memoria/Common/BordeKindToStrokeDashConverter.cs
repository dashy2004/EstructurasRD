using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LosasPlus.Models;

namespace MemoriaPlus.Common;

/// <summary>
/// Convierte un <see cref="BorderKind"/> en un <see cref="DoubleCollection"/>
/// para la propiedad <c>StrokeDashArray</c> de un Path/Line en WPF.
/// Pensado como conversor primitivo cuando ya se tiene el BorderKind aislado;
/// para iconos de tipo de losa (donde se parte de un <see cref="TipoLosa"/>),
/// usar <see cref="TipoLosaBordeStrokeDashConverter"/> que combina ambos pasos.
/// </summary>
public sealed class BordeKindToStrokeDashConverter : IValueConverter
{
    private static readonly DoubleCollection _continuo   = new();
    private static readonly DoubleCollection _puntoLargo = new() { 4, 2 };
    private static readonly DoubleCollection _puntoFino  = new() { 1, 2 };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not BorderKind bk) return _continuo;
        return bk switch
        {
            BorderKind.Empotrado => _continuo,
            BorderKind.Apoyado   => _continuo,
            BorderKind.Vuelo     => _puntoLargo,
            BorderKind.Libre     => _puntoFino,
            _                    => _continuo,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Convierte un <see cref="BorderKind"/> en grosor (StrokeThickness) para
/// contrastar visualmente cada borde del icono: empotrado=grueso, apoyado=
/// fino, vuelo/libre=fino punteado.
/// </summary>
public sealed class BordeKindToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not BorderKind bk) return 1.0;
        return bk switch
        {
            BorderKind.Empotrado => 3.0,
            BorderKind.Apoyado   => 1.2,
            _                    => 1.0,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Para un <see cref="TipoLosa"/> + índice de borde (0=N, 1=E, 2=S, 3=W como
/// <c>ConverterParameter</c>), devuelve el <see cref="DoubleCollection"/>
/// adecuado para <c>StrokeDashArray</c>. Combina los dos pasos
/// (TipoLosa→BorderKind→DashArray) para simplificar el XAML del icono.
/// </summary>
public sealed class TipoLosaBordeStrokeDashConverter : IValueConverter
{
    private static readonly BordeKindToStrokeDashConverter _inner = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TipoLosa t) return _inner.Convert(BorderKind.Apoyado, targetType, parameter, culture);
        var idx = ParseIndex(parameter);
        if (idx < 0 || idx > 3) idx = 0;
        return _inner.Convert(t.Bordes[idx], targetType, parameter, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static int ParseIndex(object? p) =>
        p is null || !int.TryParse(p.ToString(), out var i) ? 0 : i;
}

/// <summary>
/// Para un <see cref="TipoLosa"/> + índice de borde, devuelve el grosor
/// (double) que <c>StrokeThickness</c> espera.
/// </summary>
public sealed class TipoLosaBordeThicknessConverter : IValueConverter
{
    private static readonly BordeKindToThicknessConverter _inner = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TipoLosa t) return 1.0;
        var idx = ParseIndex(parameter);
        if (idx < 0 || idx > 3) idx = 0;
        return _inner.Convert(t.Bordes[idx], targetType, parameter, culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static int ParseIndex(object? p) =>
        p is null || !int.TryParse(p.ToString(), out var i) ? 0 : i;
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Paleta cromática del Módulo de Muros (Epic v1.4.0). Asigna un color
/// diferenciado a cada espesor de muro para la codificación visual del
/// lienzo CAD y la leyenda «Suma de Colores».
///
/// <para>
/// Vive en la capa de presentación (<c>src/</c>) porque produce
/// <see cref="SolidColorBrush"/> — los modelos de <c>src.Core</c> se
/// mantienen agnósticos de la UI. Port a Avalonia: <c>Brush.Freeze()</c>
/// no existe (los pinceles son ligeros/inmutables en uso); se omite.
/// </para>
/// </summary>
public static class PaletaMuros
{
    private static readonly Dictionary<int, SolidColorBrush> _porCm = new();
    private static readonly SolidColorBrush _fallback;

    static PaletaMuros()
    {
        static SolidColorBrush Pincel(byte r, byte g, byte b)
            => new(Color.FromRgb(r, g, b));

        // Espesor (cm) → color. Los espesores típicos de muro estructural.
        _porCm[10] = Pincel(0x42, 0x85, 0xF4);   // azul    — 10 cm
        _porCm[15] = Pincel(0x34, 0xA8, 0x53);   // verde   — 15 cm
        _porCm[20] = Pincel(0xFB, 0xBC, 0x05);   // ámbar   — 20 cm
        _porCm[25] = Pincel(0xEA, 0x43, 0x35);   // rojo    — 25 cm
        _porCm[30] = Pincel(0x9C, 0x27, 0xB0);   // púrpura — 30 cm
        _fallback  = Pincel(0x75, 0x75, 0x75);   // gris    — otros espesores
    }

    /// <summary>
    /// Pincel correspondiente a un espesor de muro en metros. El espesor se
    /// redondea al centímetro para elegir el bucket; los espesores fuera del
    /// catálogo reciben el color gris de reserva.
    /// </summary>
    public static SolidColorBrush BrushParaEspesor(double espesorMetros)
    {
        int cm = (int)Math.Round(espesorMetros * 100.0);
        return _porCm.TryGetValue(cm, out var brush) ? brush : _fallback;
    }
}

/// <summary>
/// Convierte un espesor de muro (m, <see cref="double"/>) en el
/// <see cref="SolidColorBrush"/> que <see cref="PaletaMuros"/> le asigna.
/// Lo usa el swatch de color de la leyenda «Suma de Colores» en
/// <c>CadView.axaml</c>.
/// </summary>
public sealed class EspesorABrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => PaletaMuros.BrushParaEspesor(value is double d ? d : 0.0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Convierte un conteo (<see cref="int"/>) en <see cref="bool"/>: <c>true</c> si es
/// mayor que cero. Lo usa la leyenda «Suma de Colores» en <c>CadView.axaml</c> para
/// mostrarse sólo cuando hay al menos un muro (Avalonia no auto-convierte int→bool).
/// </summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

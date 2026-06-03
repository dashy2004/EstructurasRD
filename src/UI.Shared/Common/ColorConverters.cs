using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Shared.UI.Common;

/// <summary>Converters de color para la UI.</summary>
public static class ColorConverters
{
    /// <summary>
    /// String hex (#RRGGBB) → brush. Vacío/invalid → transparente. Usado por el
    /// chip de preview del color de acento en Apariencia.
    /// </summary>
    public static readonly IValueConverter HexToBrush =
        new FuncValueConverter<string?, IBrush>(hex =>
        {
            if (string.IsNullOrWhiteSpace(hex)) return Brushes.Transparent;
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return Brushes.Transparent; }
        });

    private static readonly IBrush _error = new SolidColorBrush(Color.Parse("#BA1A1A"));
    private static readonly IBrush _variant = new SolidColorBrush(Color.Parse("#434751"));

    /// <summary>bool EspesorInsuficiente → rojo error / gris variante. Reemplaza
    /// el DataTrigger sobre el foreground de las celdas HCALC / H USAR.</summary>
    public static readonly IValueConverter InsuficienteForeground =
        new FuncValueConverter<bool, IBrush>(insuf => insuf ? _error : _variant);
}

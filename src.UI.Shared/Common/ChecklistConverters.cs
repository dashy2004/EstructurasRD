using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MemoriaPlus.Common;

/// <summary>
/// Converters para el checklist real de "Verificaciones previas" (Fase C).
/// Exponen el estado como texto accesible y color.
/// </summary>
public static class ChecklistConverters
{
    public static readonly IValueConverter CheckLabel =
        new FuncValueConverter<bool, string>(ok => ok ? "Disponible" : "Pendiente");

    /// <summary>
    /// true → verde éxito ; false → gris atenuado. Colores fijos (no dependen
    /// del tema) para que el converter sea autocontenido y testeable.
    /// </summary>
    public static readonly IValueConverter CheckBrush =
        new FuncValueConverter<bool, IBrush>(ok =>
            ok ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))   // verde 800
               : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E))); // gris 500
}

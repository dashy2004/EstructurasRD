using System.IO;
using System.Linq;
using OxyPlot;
using OxyPlot.ImageSharp;
using SixLabors.Fonts;

namespace LosasPlus.Rendering;

/// <summary>
/// Renderiza un <see cref="PlotModel"/> de OxyPlot a PNG en memoria con el
/// exportador de OxyPlot-core sobre SixLabors.ImageSharp (puro .NET, headless,
/// sin SkiaSharp → no toca el pin 2.88).
///
/// <para><b>Por qué existe.</b> El control <c>oxy:PlotView</c> de
/// OxyPlot.Avalonia 2.1.0-Avalonia11 tiene un bug de render: no pinta ciertas
/// series (cortante, deflexión) aunque el <see cref="PlotModel"/> y sus datos
/// sean correctos — lo confirmó la paridad con el <c>SvgExporter</c> de
/// OxyPlot-core, que sí las dibuja todas. Este helper exporta el MISMO modelo a
/// imagen por ese camino que sí funciona; la vista muestra el PNG en un
/// <c>&lt;Image&gt;</c>, esquivando el renderer roto.</para>
///
/// <para>Tipo <b>puro</b> — testeable sin display.</para>
/// </summary>
public static class DiagramaPng
{
    /// <summary>
    /// Exporta <paramref name="modelo"/> a PNG (bytes) de <paramref name="ancho"/>×
    /// <paramref name="alto"/> px a <paramref name="resolucion"/> DPI. Devuelve
    /// <see langword="null"/> si el modelo es nulo o las dimensiones no son positivas.
    /// </summary>
    public static byte[]? Render(PlotModel? modelo, int ancho, int alto, double resolucion = 96)
    {
        if (modelo is null || ancho <= 0 || alto <= 0) return null;
        // OxyPlot-core usa "Arial" por defecto; SixLabors.ImageSharp lanza si esa
        // familia no está instalada y NO hace fallback solo. Forzamos una fuente
        // que exista en el sistema (Linux: Liberation/DejaVu; Windows: Arial/Segoe).
        if (FuenteDisponible is not null) modelo.DefaultFont = FuenteDisponible;
        using var ms = new MemoryStream();
        var exportador = new PngExporter(ancho, alto, resolucion);
        exportador.Export(modelo, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Primera familia de fuentes presente en el sistema, en orden de preferencia
    /// (multiplataforma). <see langword="null"/> solo si no hay ninguna fuente —
    /// improbable. Se resuelve una vez (las fuentes no cambian en runtime).
    /// </summary>
    private static readonly string? FuenteDisponible = ElegirFuente();

    private static string? ElegirFuente()
    {
        string[] preferidas =
        {
            "Arial", "Liberation Sans", "DejaVu Sans", "Nimbus Sans",
            "Noto Sans", "Segoe UI", "Helvetica",
        };
        foreach (var nombre in preferidas)
            if (SystemFonts.TryGet(nombre, out _))
                return nombre;
        // Último recurso: cualquier familia instalada.
        return SystemFonts.Families.FirstOrDefault().Name is { Length: > 0 } n ? n : null;
    }
}

using System;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Zapatas;

namespace LosasPlus.Grillas;

/// <summary>
/// Motor estático puro de creación interactiva de columnas (y zapatas
/// auto-asociadas en cota base) en un <see cref="Nivel"/> activo del
/// proyecto, con magnetización del cursor a la grilla estructural —
/// Módulo 2 Parte C Fase 3D-II del Plan Maestro de Expansión 3D.
///
/// <para>
/// Es invocado por el code-behind del visor 3D al recibir un click
/// con la herramienta de creación activa, y también por la suite
/// xUnit que valida la mutación segura del dominio sin levantar
/// hilo UI. Encapsula tres responsabilidades atómicas:
/// <list type="number">
///   <item>Aplicar snap a la intersección de grilla más cercana vía
///   <see cref="GridSnapEngine.CalcularSnapAGrilla"/>.</item>
///   <item>Generar un <c>Id</c> entero único autoincremental.</item>
///   <item>Instanciar y agregar la <see cref="Columna"/> al nivel,
///   con sus <c>PosX</c>/<c>PosY</c> populadas para que el
///   <c>GrafoProyectadoBuilder</c> la posicione correctamente.
///   Si el nivel es base (<c>|Cota| &lt; 1e-6</c>), instancia
///   también una <see cref="ZapataAislada"/> pareada con el mismo
///   <c>Id</c> y las mismas coordenadas.</item>
/// </list>
/// </para>
///
/// <para>
/// Tipo <b>puro de dominio</b> — sin dependencias de WPF, HelixToolkit
/// ni DirectX. Vive en <c>src.Core/Grillas/</c>.
/// </para>
/// </summary>
public static class GridCreationEngine
{
    /// <summary>Tolerancia (m) para considerar la cota del nivel como "base".</summary>
    private const double ToleranciaCotaBaseM = 1e-6;

    /// <summary>
    /// Crea una nueva <see cref="Columna"/> en <paramref name="nivel"/>
    /// tras aplicar snap a <paramref name="grilla"/>. Si la cota del
    /// nivel es prácticamente cero, también crea una
    /// <see cref="ZapataAislada"/> pareada con el mismo Id y las mismas
    /// coordenadas. Ambas entidades se agregan a las colecciones del
    /// nivel atómicamente.
    /// </summary>
    /// <param name="nivel">Nivel al que se agregan los elementos creados.</param>
    /// <param name="grilla">Grilla estructural usada para el snap.</param>
    /// <param name="clickX">Coordenada X del click del cursor en el plano del nivel (m).</param>
    /// <param name="clickY">Coordenada Y idem.</param>
    /// <param name="radioToleranciaM">Radio de magnetización (m); default <see cref="GridSnapEngine.RadioToleranciaDefaultM"/>.</param>
    /// <returns>
    /// Tupla con la columna creada y, opcionalmente, la zapata pareada
    /// (no <c>null</c> sólo cuando el nivel es base).
    /// </returns>
    public static (Columna NuevaColumna, ZapataAislada? ZapataPareada) CrearColumnaConSnap(
        Nivel nivel, GrillaEstructural grilla,
        double clickX, double clickY,
        double radioToleranciaM = GridSnapEngine.RadioToleranciaDefaultM)
    {
        ArgumentNullException.ThrowIfNull(nivel);

        // 1. Snap (puede no magnetizar si está fuera de tolerancia).
        var (x, y, _) = GridSnapEngine.CalcularSnapAGrilla(
            clickX, clickY, grilla, radioToleranciaM);

        // 2. Id autoincremental: MAX(c.Id) + 1, mínimo 1.
        int nuevoId = 1;
        foreach (var c in nivel.Columnas)
            if (c.Id >= nuevoId) nuevoId = c.Id + 1;

        // 3. Instanciar columna y agregarla.
        var col = new Columna
        {
            Id     = nuevoId,
            Nombre = $"C-{nuevoId}",
            Altura = 3.0,           // m — default coherente con resto del dominio
            PosX   = x,
            PosY   = y,
            // Geometria/Acero quedan default (Módulo 3 los detallará).
        };
        nivel.Columnas.Add(col);

        // 4. Regla de negocio: nivel base ⇒ zapata pareada.
        ZapataAislada? zap = null;
        if (Math.Abs(nivel.Cota) < ToleranciaCotaBaseM)
        {
            zap = new ZapataAislada
            {
                Id     = nuevoId,    // mismo Id por convención del GrafoProyectadoBuilder
                Nombre = $"Z-{nuevoId}",
                PosX   = x,
                PosY   = y,
            };
            nivel.Zapatas.Add(zap);
        }

        return (col, zap);
    }
}

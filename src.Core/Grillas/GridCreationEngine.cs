using System;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Vigas;
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

    /// <summary>
    /// Crea una nueva <see cref="Viga"/> entre dos puntos del plano del
    /// nivel, tras aplicar snap a la grilla en ambos endpoints. La viga
    /// resultante tiene un único <see cref="TramoViga"/> con la longitud
    /// euclidiana post-snap y dos <see cref="ApoyoViga"/> en los extremos
    /// (<see cref="TipoApoyo.Fijo"/>). El Id es autoincremental
    /// <c>MAX(v.Id) + 1</c> dentro del nivel.
    ///
    /// <para>
    /// <b>Validación defensiva (Liga B paso B3, directiva 1)</b>:
    /// retorna <c>null</c> sin lanzar excepción si:
    /// <list type="bullet">
    ///   <item>cualquier coordenada es <c>NaN</c> o <c>Infinity</c>;</item>
    ///   <item>la longitud post-snap es menor a 1 cm (viga degenerada).</item>
    /// </list>
    /// El consumidor debe interpretar el <c>null</c> como rechazo
    /// silencioso (típicamente loggear y abortar la creación sin
    /// notificar al usuario con un error modal).
    /// </para>
    /// </summary>
    /// <param name="nivel">Nivel destino — la viga se agrega a <c>nivel.Vigas</c>.</param>
    /// <param name="grilla">Grilla estructural usada para el snap de ambos endpoints.</param>
    /// <param name="x1">Coordenada X del primer endpoint (m).</param>
    /// <param name="y1">Coordenada Y del primer endpoint (m).</param>
    /// <param name="x2">Coordenada X del segundo endpoint (m).</param>
    /// <param name="y2">Coordenada Y del segundo endpoint (m).</param>
    /// <param name="radioToleranciaM">Radio de magnetización por defecto.</param>
    /// <returns>La viga creada y agregada al nivel, o <c>null</c> si rechazó el payload.</returns>
    public static Viga? CrearVigaEntreSnaps(
        Nivel nivel, GrillaEstructural grilla,
        double x1, double y1, double x2, double y2,
        double radioToleranciaM = GridSnapEngine.RadioToleranciaDefaultM)
    {
        ArgumentNullException.ThrowIfNull(nivel);

        // 1. Validación de frontera — NaN/Infinity → null silencioso.
        if (double.IsNaN(x1) || double.IsNaN(y1) || double.IsNaN(x2) || double.IsNaN(y2)
            || double.IsInfinity(x1) || double.IsInfinity(y1)
            || double.IsInfinity(x2) || double.IsInfinity(y2))
            return null;

        // 2. Snap de ambos endpoints (la magnetización puede fallar
        // silenciosamente si están fuera de tolerancia — se preservan
        // coords libres).
        var (sx1, sy1, _) = GridSnapEngine.CalcularSnapAGrilla(x1, y1, grilla, radioToleranciaM);
        var (sx2, sy2, _) = GridSnapEngine.CalcularSnapAGrilla(x2, y2, grilla, radioToleranciaM);

        // 3. Longitud euclidiana post-snap; rechaza vigas degeneradas
        // (menor a 1 cm es claramente un doble-click accidental).
        double dx = sx2 - sx1, dy = sy2 - sy1;
        double longitud = Math.Sqrt(dx * dx + dy * dy);
        if (longitud < 0.01) return null;

        // 4. Id autoincremental: MAX(v.Id) + 1, mínimo 1.
        int nuevoId = 1;
        foreach (var v in nivel.Vigas)
            if (v.Id >= nuevoId) nuevoId = v.Id + 1;

        // 5. Instanciar viga con 1 tramo + 2 apoyos extremos.
        var viga = new Viga { Id = nuevoId, Nombre = $"V-{nuevoId}" };
        viga.Tramos.Add(new TramoViga { Longitud = longitud });
        viga.Apoyos.Add(new ApoyoViga(0.0,      TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(longitud, TipoApoyo.Fijo));
        nivel.Vigas.Add(viga);
        return viga;
    }
}

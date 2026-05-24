// =====================================================================
// RatioCalculator — Recorre el Proyecto y calcula el ratio
// Demanda/Capacidad por elemento invocando los motores estáticos
// existentes en `src.Core`. Devuelve un diccionario indexado por
// DomainKey para que SafCustomSheet pueda volcarlo a la hoja custom.
//
// Fase INTEROP-I1 (Parte B) del Plan Maestro de Expansión 3D —
// EstructurasRD.
//
// Decisiones de diseño:
//   1. RÉPLICA DEL PIPELINE DE ProyectoAuditorEngine: usamos las MISMAS
//      llamadas que el motor de auditoría oficial (`VigaContinuaEngine.Resolver`
//      + `RcDesignEngine.VerificarVigaCompleta`, `ColumnaDesignEngine.
//      ConstruirDiagramaInteraccion2D` + `VerificarPuntoDemanda`,
//      `ZapataDesignEngine.VerificarEstructuraZapata`). Garantiza
//      consistencia numérica con el panel de Auditoría.
//   2. TOLERANCIA A FALLOS PER-ELEMENTO: cada bloque envuelto en
//      try/catch — si un elemento tiene datos inválidos su ratio se
//      asigna a 0.0 y se continúa. Un solo elemento problemático no
//      debe abortar el export completo.
//   3. ELEMENTOS SIN DEMANDAS / SIN TRAMOS: se mapean a 0.0 (no se
//      saltean — la hoja custom necesita la fila para todos los
//      elementos del proyecto).
// =====================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Topologia;
using LosasPlus.Zapatas;

namespace LosasPlus.Interop.SAF;

/// <summary>
/// Calcula los ratios D/C de Vigas, Columnas y Zapatas de un proyecto
/// invocando los motores estáticos del dominio. Devuelve un mapa
/// inmutable <see cref="DomainKey"/> → ratio máximo del elemento.
///
/// <para>
/// Visibilidad <c>public</c> desde Liga B paso B4 — el shell
/// <c>MainViewModel</c> consume <see cref="CalcularRatioPorElemento"/>
/// vía <c>IRatioCalculatorService</c> para alimentar el panel
/// persistente "Elemento Activo".
/// </para>
/// </summary>
public static class RatioCalculator
{
    /// <summary>
    /// Resistencia del concreto por defecto cuando el dominio no la
    /// expone a nivel de proyecto, en MPa. Coincide con el default que
    /// usa <c>ProyectoAuditorEngine.AuditarProyecto</c>.
    /// </summary>
    public const double FcGlobalPorDefecto = 28.0;

    /// <summary>
    /// Fluencia del acero por defecto, en MPa (Grado 60). Idem comentario
    /// anterior.
    /// </summary>
    public const double FyGlobalPorDefecto = 420.0;

    /// <summary>
    /// Recorre el proyecto y devuelve un diccionario
    /// <c>(TipoElemento, Id) → ratio D/C máximo</c> por elemento. Los
    /// elementos sin demandas, sin tramos o con engines que fallan
    /// reciben ratio <c>0.0</c>.
    /// </summary>
    public static IReadOnlyDictionary<DomainKey, double> CalcularRatiosPorElemento(
        Proyecto proyecto,
        double fcGlobal = FcGlobalPorDefecto,
        double fyGlobal = FyGlobalPorDefecto)
    {
        ArgumentNullException.ThrowIfNull(proyecto);
        var ratios = new Dictionary<DomainKey, double>();

        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                foreach (var viga in nivel.Vigas)
                    ratios[new DomainKey(TipoElemento.Viga, viga.Id)]
                        = CalcularRatioVigaSeguro(viga, proyecto.Combinaciones);

                foreach (var col in nivel.Columnas)
                    ratios[new DomainKey(TipoElemento.Columna, col.Id)]
                        = CalcularRatioColumnaSeguro(col);

                foreach (var zap in nivel.Zapatas)
                    ratios[new DomainKey(TipoElemento.Zapata, zap.Id)]
                        = CalcularRatioZapataSeguro(zap, proyecto.Combinaciones, fcGlobal, fyGlobal);
            }
        }

        return ratios;
    }

    /// <summary>
    /// Calcula el ratio D/C de un único elemento identificado por
    /// <paramref name="key"/>. Liga B paso B4 — alimenta el panel
    /// "Elemento Activo" del shell sin recomputar todo el proyecto.
    ///
    /// <para>
    /// <b>Semánticas defensivas</b>: NUNCA lanza excepción. Retorna
    /// <see cref="double.NaN"/> ante cualquier condición errónea:
    /// elemento no encontrado, tipo no soportado (Muro/Losa), helper
    /// del engine que retorna 0.0 (interpretado como "sin demandas
    /// válidas o error interno"), o singularidad numérica catchada
    /// por el try/catch global. La UI mapea NaN al placeholder "—".
    /// </para>
    ///
    /// <para>
    /// <b>Diferencia con <see cref="CalcularRatiosPorElemento"/></b>:
    /// el método batch retorna <c>0.0</c> tanto para "sin demandas"
    /// como para "exception". El método per-elemento mapea ambos a
    /// NaN porque la UI necesita distinguir "calculado limítrofe"
    /// (ratio ~0) de "no calculable" (NaN). Si una iteración futura
    /// quiere distinguir las dos causas, los helpers privados deberían
    /// retornar <c>double?</c> — fuera de alcance B4.
    /// </para>
    /// </summary>
    public static double CalcularRatioPorElemento(
        Proyecto proyecto, DomainKey key,
        double fcGlobal = FcGlobalPorDefecto,
        double fyGlobal = FyGlobalPorDefecto)
    {
        ArgumentNullException.ThrowIfNull(proyecto);
        try
        {
            foreach (var edif in proyecto.Edificios)
            foreach (var niv in edif.Niveles)
            {
                switch (key.Tipo)
                {
                    case TipoElemento.Viga:
                    {
                        var v = niv.Vigas.FirstOrDefault(x => x.Id == key.Id);
                        if (v is not null)
                        {
                            double r = CalcularRatioVigaSeguro(v, proyecto.Combinaciones);
                            return r == 0.0 ? double.NaN : r;
                        }
                        break;
                    }
                    case TipoElemento.Columna:
                    {
                        var c = niv.Columnas.FirstOrDefault(x => x.Id == key.Id);
                        if (c is not null)
                        {
                            double r = CalcularRatioColumnaSeguro(c);
                            return r == 0.0 ? double.NaN : r;
                        }
                        break;
                    }
                    case TipoElemento.Zapata:
                    {
                        var z = niv.Zapatas.FirstOrDefault(x => x.Id == key.Id);
                        if (z is not null)
                        {
                            double r = CalcularRatioZapataSeguro(z, proyecto.Combinaciones, fcGlobal, fyGlobal);
                            return r == 0.0 ? double.NaN : r;
                        }
                        break;
                    }
                    // Muro y Losa quedan fuera de alcance — retornan NaN.
                }
            }
        }
        catch
        {
            // Defensa última — singularidades inesperadas degradadas a NaN.
        }
        return double.NaN;
    }

    // =================================================================
    // VIGAS
    // =================================================================

    /// <summary>
    /// Resuelve la viga continua, evalúa la envolvente y devuelve el
    /// <c>RatioMaximo</c> de la verificación RC. Tolera vigas sin tramos
    /// o inestables (devuelve <c>0.0</c>).
    /// </summary>
    private static double CalcularRatioVigaSeguro(
        LosasPlus.Vigas.Viga viga, LosasPlus.Cargas.CombinacionesProyecto combinaciones)
    {
        try
        {
            if (viga.Tramos.Count == 0) return 0.0;
            var resultado = VigaContinuaEngine.Resolver(viga, combinaciones);
            if (resultado.EsInestable || resultado.Envolvente is null) return 0.0;
            var verif = RcDesignEngine.VerificarVigaCompleta(viga, resultado.Envolvente);
            return verif.RatioMaximo;
        }
        catch
        {
            // El export NO debe abortar por un elemento corrupto. La
            // auditoría oficial reportará el problema al usuario.
            return 0.0;
        }
    }

    // =================================================================
    // COLUMNAS
    // =================================================================

    /// <summary>
    /// Construye el diagrama P-M de la columna, itera sobre todas sus
    /// demandas y devuelve el máximo <c>RatioEficiencia</c>. Columnas sin
    /// demandas reciben <c>0.0</c>.
    /// </summary>
    private static double CalcularRatioColumnaSeguro(Columna col)
    {
        try
        {
            if (col.Demandas.Count == 0) return 0.0;
            var diagrama = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(
                col.Geometria, col.Acero);
            double ratioMax = 0.0;
            foreach (var d in col.Demandas)
            {
                var r = ColumnaDesignEngine.VerificarPuntoDemanda(diagrama, d.Mu, d.Pu);
                if (double.IsFinite(r.RatioEficiencia) && r.RatioEficiencia > ratioMax)
                    ratioMax = r.RatioEficiencia;
            }
            return ratioMax;
        }
        catch
        {
            return 0.0;
        }
    }

    // =================================================================
    // ZAPATAS
    // =================================================================

    /// <summary>
    /// Verifica la zapata aislada y devuelve <c>RatioEstructuralMaximo</c>
    /// (máximo de flexión / cortante / punzonamiento). Zapatas con
    /// engine fallido reciben <c>0.0</c>.
    /// </summary>
    private static double CalcularRatioZapataSeguro(
        ZapataAislada zapata,
        LosasPlus.Cargas.CombinacionesProyecto combinaciones,
        double fcGlobal, double fyGlobal)
    {
        try
        {
            var r = ZapataDesignEngine.VerificarEstructuraZapata(
                zapata, combinaciones, fcGlobal, fyGlobal);
            return r.RatioEstructuralMaximo;
        }
        catch
        {
            return 0.0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Zapatas;

namespace LosasPlus.Services;

/// <summary>
/// Motor de análisis de presiones de contacto de una <see cref="ZapataAislada"/>
/// sometida a flexocompresión biaxial bajo cada combinación de servicio del
/// proyecto (Fase 6, Iteración 1 de la suite estructural).
///
/// <para>
/// Clase estática <b>pura</b> — sin estado ni dependencias de UI. Bajo la
/// hipótesis de zapata rígida sobre suelo elástico, la presión de contacto en
/// un punto <c>(x, y)</c> de la base es lineal:
/// <c>q(x, y) = P/A + Mx · y / Ix + My · x / Iy</c>. El motor evalúa esta
/// presión en las cuatro esquinas de la base para cada combinación marcada
/// como <see cref="TipoCombinacion.Servicio"/>, le suma el peso propio del
/// dado de concreto y el peso del suelo de relleno sobre la zapata, y reporta
/// la presión máxima, mínima, el despegue (q_min &lt; 0) y la fracción del
/// área en tracción.
/// </para>
///
/// <para>
/// Unidades: longitudes en m, fuerzas en kN, momentos en kN·m, presiones en
/// kN/m². Convenciones de signo: <c>P</c> positivo = compresión sobre la
/// zapata; <c>Mx</c> positivo comprime el lado <c>+y</c>; <c>My</c> positivo
/// comprime el lado <c>+x</c>.
/// </para>
/// </summary>
public static class ZapataDesignEngine
{
    /// <summary>Peso específico nominal del concreto reforzado, en kN/m³.</summary>
    private const double PesoEspecificoConcreto = 24.0;

    /// <summary>
    /// Analiza las presiones de contacto que la <paramref name="zapata"/>
    /// ejerce sobre el suelo bajo cada combinación de servicio de
    /// <paramref name="combinaciones"/>. Devuelve
    /// <see cref="ResultadoPresionesZapata.Vacio"/> si la zapata o el terreno
    /// tienen datos inválidos o el proyecto no incluye combinaciones de
    /// servicio.
    /// </summary>
    public static ResultadoPresionesZapata AnalizarPresionesDeContacto(
        ZapataAislada zapata, CombinacionesProyecto combinaciones)
    {
        ArgumentNullException.ThrowIfNull(zapata);
        ArgumentNullException.ThrowIfNull(combinaciones);

        var g = zapata.Dimensiones;
        var t = zapata.Suelo;

        if (g.LargoB <= 0.0 || g.AnchoL <= 0.0 || g.EspesorH <= 0.0
            || t.PresionAdmisible <= 0.0)
            return ResultadoPresionesZapata.Vacio;

        // --- Geometría y módulos resistentes ---
        double B = g.LargoB;
        double L = g.AnchoL;
        double H = g.EspesorH;
        double Df = g.ProfundidadDesplante;
        double A = B * L;
        double Wx = B * L * L / 6.0;
        double Wy = L * B * B / 6.0;

        // --- Carga permanente: peso del dado + peso del suelo de relleno ---
        double pesoZapata = PesoEspecificoConcreto * B * L * H;
        double areaCol = Math.Max(0.0, g.PeralteColumna * g.AnchoColumna);
        double espesorRelleno = Math.Max(0.0, Df - H);
        double areaRelleno = Math.Max(0.0, A - areaCol);
        double pesoTerreno = t.PesoEspecificoSuelo * espesorRelleno * areaRelleno;
        double Ppermanente = pesoZapata + pesoTerreno;

        // --- Filtrar combinaciones de servicio ---
        var combosServicio = combinaciones.Combinaciones
            .Where(c => c.Tipo == TipoCombinacion.Servicio)
            .ToList();
        if (combosServicio.Count == 0)
            return ResultadoPresionesZapata.Vacio;

        // --- Evaluar cada combinación ---
        var porCombinacion = new List<PresionEsquinaPorCombinacion>(combosServicio.Count);
        double qMaxGlobal = double.NegativeInfinity;
        double qMinGlobal = double.PositiveInfinity;
        bool esEstable = true;

        foreach (var combo in combosServicio)
        {
            // Sumar las cargas mayoradas por el factor de la combinación.
            double Pt = Ppermanente;
            double Mxt = 0.0;
            double Myt = 0.0;
            foreach (var c in zapata.Cargas)
            {
                double factor = combo[c.CodigoCaso];
                if (factor == 0.0) continue;
                Pt += factor * c.P;
                Mxt += factor * c.Mx;
                Myt += factor * c.My;
            }

            // Presiones en las cuatro esquinas.
            double q1 = Pt / A + Mxt / Wx + Myt / Wy;   // (+B/2, +L/2) — NE
            double q2 = Pt / A + Mxt / Wx - Myt / Wy;   // (−B/2, +L/2) — NW
            double q3 = Pt / A - Mxt / Wx - Myt / Wy;   // (−B/2, −L/2) — SW
            double q4 = Pt / A - Mxt / Wx + Myt / Wy;   // (+B/2, −L/2) — SE

            double qMax = Math.Max(Math.Max(q1, q2), Math.Max(q3, q4));
            double qMin = Math.Min(Math.Min(q1, q2), Math.Min(q3, q4));

            bool excede = qMax > t.PresionAdmisible;
            bool despegue = qMin < 0.0;
            double pctTraccion = despegue
                ? CalcularPorcentajeAreaEnTraccion(q1, q2, q3, q4)
                : 0.0;

            porCombinacion.Add(new PresionEsquinaPorCombinacion(
                NombreCombinacion: combo.Nombre,
                Q1: q1, Q2: q2, Q3: q3, Q4: q4,
                Maxima: qMax,
                Minima: qMin,
                ExcedePresionAdmisible: excede,
                HayDespegue: despegue,
                PorcentajeAreaEnTraccion: pctTraccion));

            if (qMax > qMaxGlobal) qMaxGlobal = qMax;
            if (qMin < qMinGlobal) qMinGlobal = qMin;
            if (excede) esEstable = false;
        }

        return new ResultadoPresionesZapata(
            porCombinacion, qMaxGlobal, qMinGlobal, esEstable);
    }

    // ----- Helpers privados -----

    /// <summary>
    /// Fracción del área de la base que pierde contacto con el suelo (q &lt; 0)
    /// dadas las presiones en las cuatro esquinas. Recorta el rectángulo
    /// unitario por la línea <c>q = 0</c> y devuelve <c>1 − área_en_contacto</c>.
    /// La distribución de presiones es lineal sobre el rectángulo, así que un
    /// solo pase de recorte tipo Sutherland-Hodgman es suficiente.
    /// </summary>
    private static double CalcularPorcentajeAreaEnTraccion(
        double q1, double q2, double q3, double q4)
    {
        // Esquinas CCW del rectángulo normalizado [0,1]×[0,1]:
        //   (1,1) ≡ (+B/2,+L/2),  (0,1) ≡ (−B/2,+L/2),
        //   (0,0) ≡ (−B/2,−L/2),  (1,0) ≡ (+B/2,−L/2).
        Span<(double x, double y, double q)> corners = stackalloc[]
        {
            (1.0, 1.0, q1),
            (0.0, 1.0, q2),
            (0.0, 0.0, q3),
            (1.0, 0.0, q4),
        };

        var contacto = new List<(double x, double y)>(8);
        int n = corners.Length;
        for (int i = 0; i < n; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % n];
            if (a.q >= 0.0) contacto.Add((a.x, a.y));
            if ((a.q >= 0.0) != (b.q >= 0.0))
            {
                // Intersección donde q cambia de signo: t·q_a + (1−t)·q_b = 0.
                double s = a.q / (a.q - b.q);
                contacto.Add((a.x + s * (b.x - a.x), a.y + s * (b.y - a.y)));
            }
        }

        double areaContacto = AreaPoligono(contacto);
        return Math.Clamp(1.0 - areaContacto, 0.0, 1.0);
    }

    /// <summary>Área absoluta del polígono cerrado mediante la fórmula del zapatero (shoelace).</summary>
    private static double AreaPoligono(IReadOnlyList<(double x, double y)> puntos)
    {
        if (puntos.Count < 3) return 0.0;
        double suma = 0.0;
        int n = puntos.Count;
        for (int i = 0; i < n; i++)
        {
            var a = puntos[i];
            var b = puntos[(i + 1) % n];
            suma += a.x * b.y - b.x * a.y;
        }
        return Math.Abs(suma) / 2.0;
    }
}

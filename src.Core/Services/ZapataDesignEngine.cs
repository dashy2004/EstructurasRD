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

    /// <summary>Recubrimiento mínimo en la cara inferior de zapatas (ACI 318-19 §20.5.1.3), en m.</summary>
    private const double RecubrimientoZapata = 0.075;

    /// <summary>Cuantía mínima de retracción/temperatura para Grade 60 (ACI 318-19 §13.3.3.1).</summary>
    private const double CuantiaMinimaFlexion = 0.0018;

    /// <summary>Factor de reducción de resistencia a cortante (ACI 318-19 §21.2).</summary>
    private const double PhiCortante = 0.75;

    /// <summary>Factor de reducción de resistencia a flexión (ACI 318-19 §21.2, tensión controlada).</summary>
    private const double PhiFlexion = 0.90;

    /// <summary>Factor α_s para columna interior en la ecuación de punzonamiento ACI 22.6.5.2.</summary>
    private const double AlfaSColumnaInterior = 40.0;

    /// <summary>
    /// Verifica las tres fallas estructurales de Estado Límite Último (ELU)
    /// de una <see cref="ZapataAislada"/> según ACI 318-19 (Fase 6,
    /// Iteración 3): flexión en la cara del pedestal, cortante unidimensional
    /// a una distancia <c>d</c> y punzonamiento biaxial a <c>d/2</c>.
    ///
    /// <para>
    /// Procesa exclusivamente las combinaciones <c>Tipo == Ultima</c> y
    /// excluye el peso permanente de los esfuerzos estructurales (el peso
    /// propio del dado de concreto y el suelo de relleno se cancelan con la
    /// presión recíproca del terreno justo debajo de la zapata). Aplica
    /// <c>qu_max</c> uniforme conservador sobre las áreas tributarias.
    /// </para>
    /// </summary>
    /// <param name="zapata">La zapata a verificar (geometría + cargas).</param>
    /// <param name="combinaciones">Base de combinaciones del proyecto.</param>
    /// <param name="fc">Resistencia especificada del concreto a compresión f'c, en MPa.</param>
    /// <param name="fy">Esfuerzo de fluencia del acero de refuerzo fy, en MPa.</param>
    public static ResultadoEstructuralZapata VerificarEstructuraZapata(
        ZapataAislada zapata, CombinacionesProyecto combinaciones, double fc, double fy)
    {
        ArgumentNullException.ThrowIfNull(zapata);
        ArgumentNullException.ThrowIfNull(combinaciones);

        var g = zapata.Dimensiones;
        if (g.LargoB <= 0.0 || g.AnchoL <= 0.0 || g.EspesorH <= 0.0 || fc <= 0.0 || fy <= 0.0)
            return ResultadoEstructuralZapata.Vacio;

        // --- Geometría ---
        double B = g.LargoB;
        double L = g.AnchoL;
        double H = g.EspesorH;
        double d = H - RecubrimientoZapata;
        if (d <= 0.0) return ResultadoEstructuralZapata.Vacio;

        double A = B * L;
        double Wx = B * L * L / 6.0;
        double Wy = L * B * B / 6.0;
        double bCol = Math.Max(0.0, g.AnchoColumna);
        double lCol = Math.Max(0.0, g.PeralteColumna);
        double cX = (B - bCol) / 2.0;
        double cY = (L - lCol) / 2.0;
        if (cX < 0.0) cX = 0.0;
        if (cY < 0.0) cY = 0.0;

        // Perímetro crítico de punzonamiento a d/2 del pedestal (rectángulo
        // concéntrico de lados b_col+d y l_col+d).
        double ladoPerimX = bCol + d;
        double ladoPerimY = lCol + d;
        double b0 = 2.0 * (ladoPerimX + ladoPerimY);
        double areaPerim = ladoPerimX * ladoPerimY;
        double beta = Math.Max(bCol, lCol) > 0.0 && Math.Min(bCol, lCol) > 0.0
            ? Math.Max(bCol, lCol) / Math.Min(bCol, lCol)
            : 1.0;

        // --- Acero mínimo reglamentario por metro de ancho ---
        double AsMinPorMetro = CuantiaMinimaFlexion * 1.0 * H;          // m²/m
        double aMin = AsMinPorMetro * fy / (0.85 * fc * 1.0);            // m (b=1m)
        double phiMnMinPorMetro = PhiFlexion * AsMinPorMetro * fy * (d - aMin / 2.0) * 1000.0;
            // kN·m/m   (m² · MPa · m · 10³ = m³·MPa·10³ = 10³ kN·m)

        // --- Capacidades a cortante 1D (por dirección, dependen del ancho perpendicular) ---
        double phiVcX = PhiCortante * 0.17 * Math.Sqrt(fc) * L * d * 1000.0;   // kN — corte sección X (ancho L)
        double phiVcY = PhiCortante * 0.17 * Math.Sqrt(fc) * B * d * 1000.0;   // kN — corte sección Y (ancho B)

        // --- Capacidad a punzonamiento (constante para la zapata) ---
        double sqrtFc = Math.Sqrt(fc);
        double vC1 = 0.17 * (1.0 + 2.0 / beta) * sqrtFc * b0 * d * 1000.0;
        double vC2 = 0.083 * (2.0 + AlfaSColumnaInterior * d / b0) * sqrtFc * b0 * d * 1000.0;
        double vC3 = 0.33 * sqrtFc * b0 * d * 1000.0;
        double phiVcPunz = PhiCortante * Math.Min(vC1, Math.Min(vC2, vC3));

        // --- Iterar combinaciones de tipo Ultima ---
        var combosUltima = combinaciones.Combinaciones
            .Where(c => c.Tipo == TipoCombinacion.Ultima)
            .ToList();
        if (combosUltima.Count == 0)
            return ResultadoEstructuralZapata.Vacio with
            {
                LadoPerimetroX = ladoPerimX,
                LadoPerimetroY = ladoPerimY,
                AceroMinimoRequerido = AsMinPorMetro,
            };

        double ratioFlexMaxGlobal = 0.0;
        double ratioCortMaxGlobal = 0.0;
        double ratioPunzMaxGlobal = 0.0;
        double ratioMaxGlobal = 0.0;
        string nombreCritico = "";
        double mUDetalle = 0.0;
        double vUDetalle = 0.0;
        double vUpDetalle = 0.0;

        foreach (var combo in combosUltima)
        {
            // Cargas externas mayoradas — sin peso permanente.
            double Pt = 0.0, Mxt = 0.0, Myt = 0.0;
            foreach (var c in zapata.Cargas)
            {
                double factor = combo[c.CodigoCaso];
                if (factor == 0.0) continue;
                Pt += factor * c.P;
                Mxt += factor * c.Mx;
                Myt += factor * c.My;
            }

            // Presiones netas en las 4 esquinas.
            double q1 = Pt / A + Mxt / Wx + Myt / Wy;
            double q2 = Pt / A + Mxt / Wx - Myt / Wy;
            double q3 = Pt / A - Mxt / Wx - Myt / Wy;
            double q4 = Pt / A - Mxt / Wx + Myt / Wy;
            double quMax = Math.Max(Math.Max(q1, q2), Math.Max(q3, q4));

            // Si la combinación produce únicamente levantamiento, no hay
            // esfuerzos estructurales que verificar — saltar.
            if (quMax <= 0.0) continue;

            // --- Flexión: M_u por metro de ancho, ratio contra φMn_min ---
            double mUxPorMetro = quMax * cX * cX / 2.0;
            double mUyPorMetro = quMax * cY * cY / 2.0;
            double ratioFlexX = phiMnMinPorMetro > 0.0 ? mUxPorMetro / phiMnMinPorMetro : 0.0;
            double ratioFlexY = phiMnMinPorMetro > 0.0 ? mUyPorMetro / phiMnMinPorMetro : 0.0;
            double ratioFlex = Math.Max(ratioFlexX, ratioFlexY);
            double mUDetalleCombo = Math.Max(mUxPorMetro, mUyPorMetro);

            // --- Cortante 1D: V_u a una distancia d del pedestal ---
            double vUx = quMax * L * Math.Max(0.0, cX - d);
            double vUy = quMax * B * Math.Max(0.0, cY - d);
            double ratioCortX = phiVcX > 0.0 ? vUx / phiVcX : 0.0;
            double ratioCortY = phiVcY > 0.0 ? vUy / phiVcY : 0.0;
            double ratioCort = Math.Max(ratioCortX, ratioCortY);
            double vUDetalleCombo = Math.Max(vUx, vUy);

            // --- Punzonamiento: V_up = qu_max · (A − área del perímetro) ---
            double vUp = quMax * Math.Max(0.0, A - areaPerim);
            double ratioPunz = phiVcPunz > 0.0 ? vUp / phiVcPunz : 0.0;

            // --- Recordar la combinación crítica ---
            double ratioCombo = Math.Max(ratioFlex, Math.Max(ratioCort, ratioPunz));
            if (ratioCombo > ratioMaxGlobal)
            {
                ratioMaxGlobal = ratioCombo;
                nombreCritico = combo.Nombre;
                mUDetalle = mUDetalleCombo;
                vUDetalle = vUDetalleCombo;
                vUpDetalle = vUp;
            }

            if (ratioFlex > ratioFlexMaxGlobal) ratioFlexMaxGlobal = ratioFlex;
            if (ratioCort > ratioCortMaxGlobal) ratioCortMaxGlobal = ratioCort;
            if (ratioPunz > ratioPunzMaxGlobal) ratioPunzMaxGlobal = ratioPunz;
        }

        return new ResultadoEstructuralZapata(
            RatioFlexion: ratioFlexMaxGlobal,
            RatioCortante: ratioCortMaxGlobal,
            RatioPunzonamiento: ratioPunzMaxGlobal,
            RatioEstructuralMaximo: ratioMaxGlobal,
            EstructuraConforme: ratioMaxGlobal <= 1.0,
            NombreCombinacionCritica: nombreCritico,
            MomentoUltimo: mUDetalle,
            CortanteUltimo: vUDetalle,
            PunzonamientoUltimo: vUpDetalle,
            AceroMinimoRequerido: AsMinPorMetro,
            LadoPerimetroX: ladoPerimX,
            LadoPerimetroY: ladoPerimY);
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

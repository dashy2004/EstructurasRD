using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Vigas;

namespace LosasPlus.Services;

/// <summary>
/// Motor de resolución de vigas continuas hiperestáticas por el <b>Método de
/// Rigidez Directa</b> (Fase 3 de la suite estructural). Resuelve, para cada
/// combinación de carga del proyecto, los diagramas de fuerza cortante V(x),
/// momento flector M(x) y deflexión δ(x), las reacciones en los apoyos y la
/// envolvente de diseño.
///
/// <para>
/// Clase estática <b>pura</b> — sin estado ni dependencias de UI. Modelo de
/// elemento: viga de Euler-Bernoulli con 2 grados de libertad por nodo
/// (traslación vertical y rotación). Admite inercia variable entre tramos,
/// voladizos y apoyos fijos, empotrados y elásticos.
/// </para>
///
/// <para>
/// <b>Convenciones.</b> En el cálculo interno el eje vertical es positivo hacia
/// arriba; una <see cref="CargaElemento.Magnitud"/> positiva es descendente
/// (gravitatoria). En los resultados: el momento es positivo a tracción
/// inferior (<i>sagging</i>), la deflexión es positiva hacia abajo y la
/// reacción es positiva hacia arriba. Unidades base SI kN-m.
/// </para>
/// </summary>
public static class VigaContinuaEngine
{
    /// <summary>Tolerancia geométrica para fusionar nodos coincidentes (m).</summary>
    private const double ToleranciaNodo = 1e-6;

    /// <summary>Tolerancia relativa para detectar una matriz singular (mecanismo).</summary>
    private const double ToleranciaSingular = 1e-9;

    /// <summary>Un elemento finito de la discretización: el segmento entre dos nodos.</summary>
    private readonly record struct Elemento(
        int NodoA, int NodoB, double Longitud, double RigidezFlexion, int IndiceTramo);

    /// <summary>
    /// Resuelve la <paramref name="viga"/> para todas las combinaciones de
    /// <paramref name="combinaciones"/> y devuelve los diagramas, las reacciones
    /// y la envolvente de diseño.
    /// </summary>
    /// <param name="viga">La viga continua a resolver.</param>
    /// <param name="combinaciones">Base de cargas del proyecto — el motor resuelve cada combinación.</param>
    /// <param name="muestrasPorElemento">Puntos de muestreo de los diagramas por elemento finito.</param>
    /// <returns>
    /// El resultado completo; si la viga es un mecanismo,
    /// <see cref="ResultadoViga.EsInestable"/> es <c>true</c> (sin excepción).
    /// </returns>
    public static ResultadoViga Resolver(
        Viga viga, CombinacionesProyecto combinaciones, int muestrasPorElemento = 21)
    {
        ArgumentNullException.ThrowIfNull(viga);
        ArgumentNullException.ThrowIfNull(combinaciones);
        muestrasPorElemento = Math.Max(2, muestrasPorElemento);

        var tramos = viga.Tramos.ToList();
        if (tramos.Count == 0)
            return ResultadoViga.Inestable("La viga no tiene tramos definidos.");

        // --- Geometría: fronteras de tramo y longitud total ---
        var fronteras = new double[tramos.Count + 1];
        for (int i = 0; i < tramos.Count; i++)
            fronteras[i + 1] = fronteras[i] + tramos[i].Longitud;
        double longitudTotal = fronteras[^1];
        if (longitudTotal <= ToleranciaNodo)
            return ResultadoViga.Inestable("La longitud total de la viga es nula.");

        // --- Nodos: fronteras de tramo ∪ apoyos ∪ posiciones de cargas puntuales ---
        var posiciones = new List<double>(fronteras);
        foreach (var apoyo in viga.Apoyos)
            posiciones.Add(Math.Clamp(apoyo.CoordenadaX, 0.0, longitudTotal));
        for (int i = 0; i < tramos.Count; i++)
            foreach (var carga in tramos[i].Cargas)
                if (carga.Tipo == TipoCargaElemento.Puntual)
                    posiciones.Add(Math.Clamp(fronteras[i] + carga.Posicion, 0.0, longitudTotal));

        var nodosX = Deduplicar(posiciones);
        int n = nodosX.Count;
        if (n < 2)
            return ResultadoViga.Inestable("La viga necesita al menos dos nodos.");

        // --- Elementos: un segmento entre cada par de nodos consecutivos ---
        var elementos = new List<Elemento>(n - 1);
        for (int seg = 0; seg < n - 1; seg++)
        {
            double longitud = nodosX[seg + 1] - nodosX[seg];
            if (longitud <= ToleranciaNodo) continue;   // descartar segmentos nulos
            int indiceTramo = IndiceTramoEn(fronteras, 0.5 * (nodosX[seg] + nodosX[seg + 1]));
            double ei = tramos[indiceTramo].ModuloElasticidad * tramos[indiceTramo].Inercia;
            elementos.Add(new Elemento(seg, seg + 1, longitud, ei, indiceTramo));
        }
        if (elementos.Count == 0)
            return ResultadoViga.Inestable("La viga no tiene elementos válidos.");

        int gdl = 2 * n;   // 2 GDL por nodo: traslación (par) y rotación (impar)

        // --- Matriz de rigidez global K (independiente de la combinación) ---
        var k = new double[gdl, gdl];
        foreach (var e in elementos)
        {
            var ke = RigidezElemento(e.RigidezFlexion, e.Longitud);
            int[] mapa = { 2 * e.NodoA, 2 * e.NodoA + 1, 2 * e.NodoB, 2 * e.NodoB + 1 };
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    k[mapa[a], mapa[b]] += ke[a, b];
        }

        // --- Condiciones de borde: GDL restringidos y resortes elásticos ---
        var restringido = new bool[gdl];
        foreach (var apoyo in viga.Apoyos)
        {
            int nodo = NodoMasCercano(nodosX, Math.Clamp(apoyo.CoordenadaX, 0.0, longitudTotal));
            switch (apoyo.Tipo)
            {
                case TipoApoyo.Fijo:
                    restringido[2 * nodo] = true;
                    break;
                case TipoApoyo.Empotrado:
                    restringido[2 * nodo] = true;
                    restringido[2 * nodo + 1] = true;
                    break;
                case TipoApoyo.Elastico:
                    k[2 * nodo, 2 * nodo] += apoyo.RigidezResorte;
                    break;
            }
        }

        // GDL libres y submatriz de rigidez reducida K_ff.
        var libres = new List<int>();
        for (int d = 0; d < gdl; d++)
            if (!restringido[d]) libres.Add(d);
        if (libres.Count == 0)
            return ResultadoViga.Inestable("La viga está totalmente restringida.");

        int m = libres.Count;
        var kff = new double[m, m];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
                kff[i, j] = k[libres[i], libres[j]];

        // --- Resolver cada combinación de carga ---
        var resultados = new List<ResultadoCombinacion>();
        foreach (var combo in combinaciones.Combinaciones)
        {
            var fGlobal = VectorDeCarga(tramos, fronteras, elementos, nodosX, combo, gdl, longitudTotal);

            var fLibre = new double[m];
            for (int i = 0; i < m; i++) fLibre[i] = fGlobal[libres[i]];

            var uLibre = ResolverGauss(kff, fLibre);
            if (uLibre is null)
                return ResultadoViga.Inestable(
                    "La viga es un mecanismo: los apoyos no bastan para una solución estable.");

            var u = new double[gdl];
            for (int i = 0; i < m; i++) u[libres[i]] = uLibre[i];

            var diagrama = ConstruirDiagrama(tramos, elementos, nodosX, combo, u, muestrasPorElemento);
            var reacciones = ConstruirReacciones(viga, nodosX, k, u, fGlobal, gdl, longitudTotal);
            resultados.Add(new ResultadoCombinacion(combo.Nombre, combo.Tipo, diagrama, reacciones));
        }

        return new ResultadoViga(false, null, resultados, ConstruirEnvolvente(resultados));
    }

    // ----- Ensamblaje del vector de carga de una combinación -----

    private static double[] VectorDeCarga(
        List<TramoViga> tramos, double[] fronteras, List<Elemento> elementos,
        List<double> nodosX, CombinacionCarga combo, int gdl, double longitudTotal)
    {
        var f = new double[gdl];

        // Cargas puntuales → fuerza nodal directa (su posición es un nodo).
        for (int i = 0; i < tramos.Count; i++)
            foreach (var carga in tramos[i].Cargas)
            {
                if (carga.Tipo != TipoCargaElemento.Puntual) continue;
                double factor = combo[carga.CodigoCaso];
                if (factor == 0.0) continue;
                double xGlobal = Math.Clamp(fronteras[i] + carga.Posicion, 0.0, longitudTotal);
                int nodo = NodoMasCercano(nodosX, xGlobal);
                f[2 * nodo] += -(factor * carga.Magnitud);   // descendente → eje −y
            }

        // Cargas distribuidas → fuerzas de empotramiento perfecto por elemento.
        foreach (var e in elementos)
        {
            double w = IntensidadDistribuida(tramos[e.IndiceTramo], combo);
            if (w == 0.0) continue;
            double l = e.Longitud;
            f[2 * e.NodoA]     += -w * l / 2.0;
            f[2 * e.NodoA + 1] += -w * l * l / 12.0;
            f[2 * e.NodoB]     += -w * l / 2.0;
            f[2 * e.NodoB + 1] += +w * l * l / 12.0;
        }
        return f;
    }

    // ----- Reconstrucción de los diagramas V(x), M(x), δ(x) -----

    private static List<PuntoDiagrama> ConstruirDiagrama(
        List<TramoViga> tramos, List<Elemento> elementos, List<double> nodosX,
        CombinacionCarga combo, double[] u, int muestras)
    {
        var diagrama = new List<PuntoDiagrama>(elementos.Count * muestras);
        foreach (var e in elementos)
        {
            double l = e.Longitud;
            double ei = e.RigidezFlexion;
            double w = IntensidadDistribuida(tramos[e.IndiceTramo], combo);
            var ke = RigidezElemento(ei, l);
            double[] ue =
            {
                u[2 * e.NodoA], u[2 * e.NodoA + 1], u[2 * e.NodoB], u[2 * e.NodoB + 1],
            };

            // Fuerzas de extremo del elemento: f_member = k_e·u_e − f_eq_e.
            double vi = 0.0, mi = 0.0;
            for (int c = 0; c < 4; c++)
            {
                vi += ke[0, c] * ue[c];
                mi += ke[1, c] * ue[c];
            }
            vi += w * l / 2.0;          // − f_eq_e[0] = −(−w·l/2)
            mi += w * l * l / 12.0;     // − f_eq_e[1] = −(−w·l²/12)

            for (int s = 0; s < muestras; s++)
            {
                double x = l * s / (muestras - 1);
                double cortante = vi - w * x;
                double momento = -mi + vi * x - w * x * x / 2.0;
                // δ: Hermite de los GDL nodales + término particular UDL (eje +y arriba).
                double vArriba = Hermite(ue, l, x)
                                 - w * x * x * (l - x) * (l - x) / (24.0 * ei);
                diagrama.Add(new PuntoDiagrama(
                    nodosX[e.NodoA] + x, cortante, momento, -vArriba));
            }
        }
        return diagrama;
    }

    private static List<ReaccionApoyo> ConstruirReacciones(
        Viga viga, List<double> nodosX, double[,] k, double[] u, double[] f,
        int gdl, double longitudTotal)
    {
        var reacciones = new List<ReaccionApoyo>(viga.Apoyos.Count);
        foreach (var apoyo in viga.Apoyos)
        {
            int nodo = NodoMasCercano(nodosX, Math.Clamp(apoyo.CoordenadaX, 0.0, longitudTotal));
            double valor;
            if (apoyo.Tipo == TipoApoyo.Elastico)
            {
                valor = -apoyo.RigidezResorte * u[2 * nodo];
            }
            else
            {
                // Reacción del GDL restringido: R = (K·U − F).
                double ku = 0.0;
                for (int j = 0; j < gdl; j++) ku += k[2 * nodo, j] * u[j];
                valor = ku - f[2 * nodo];
            }
            reacciones.Add(new ReaccionApoyo(nodosX[nodo], valor));
        }
        return reacciones;
    }

    private static EnvolventeViga? ConstruirEnvolvente(List<ResultadoCombinacion> resultados)
    {
        if (resultados.Count == 0) return null;
        int puntos = resultados[0].Diagrama.Count;
        var lista = new List<PuntoEnvolvente>(puntos);
        double mMax = double.NegativeInfinity, mMin = double.PositiveInfinity;
        double vMax = double.NegativeInfinity, vMin = double.PositiveInfinity;
        double dMax = double.NegativeInfinity, dMin = double.PositiveInfinity;

        for (int i = 0; i < puntos; i++)
        {
            double cMax = double.NegativeInfinity, cMin = double.PositiveInfinity;
            double moMax = double.NegativeInfinity, moMin = double.PositiveInfinity;
            double deMax = double.NegativeInfinity, deMin = double.PositiveInfinity;
            foreach (var r in resultados)
            {
                var p = r.Diagrama[i];
                cMax = Math.Max(cMax, p.Cortante);   cMin = Math.Min(cMin, p.Cortante);
                moMax = Math.Max(moMax, p.Momento);  moMin = Math.Min(moMin, p.Momento);
                deMax = Math.Max(deMax, p.Deflexion); deMin = Math.Min(deMin, p.Deflexion);
            }
            lista.Add(new PuntoEnvolvente(
                resultados[0].Diagrama[i].X, cMax, cMin, moMax, moMin, deMax, deMin));
            vMax = Math.Max(vMax, cMax);   vMin = Math.Min(vMin, cMin);
            mMax = Math.Max(mMax, moMax);  mMin = Math.Min(mMin, moMin);
            dMax = Math.Max(dMax, deMax);  dMin = Math.Min(dMin, deMin);
        }
        return new EnvolventeViga(lista, mMax, mMin, vMax, vMin, dMax, dMin);
    }

    // ----- Núcleo numérico -----

    /// <summary>Matriz de rigidez 4×4 de un elemento viga de Euler-Bernoulli.</summary>
    private static double[,] RigidezElemento(double ei, double l)
    {
        double c = ei / (l * l * l);
        double l2 = l * l;
        return new double[,]
        {
            {  12 * c,      6 * l * c,   -12 * c,      6 * l * c   },
            {  6 * l * c,   4 * l2 * c,  -6 * l * c,   2 * l2 * c  },
            { -12 * c,     -6 * l * c,    12 * c,     -6 * l * c   },
            {  6 * l * c,   2 * l2 * c,  -6 * l * c,   4 * l2 * c  },
        };
    }

    /// <summary>Interpolación de Hermite de la deflexión a partir de los 4 GDL nodales.</summary>
    private static double Hermite(double[] ue, double l, double x)
    {
        double xi = x / l;
        double xi2 = xi * xi, xi3 = xi2 * xi;
        double n1 = 1.0 - 3.0 * xi2 + 2.0 * xi3;
        double n2 = l * (xi - 2.0 * xi2 + xi3);
        double n3 = 3.0 * xi2 - 2.0 * xi3;
        double n4 = l * (-xi2 + xi3);
        return n1 * ue[0] + n2 * ue[1] + n3 * ue[2] + n4 * ue[3];
    }

    /// <summary>
    /// Resuelve <c>A·x = b</c> por eliminación gaussiana con pivoteo parcial.
    /// Devuelve <c>null</c> si la matriz es singular (la viga es un mecanismo).
    /// No muta los argumentos.
    /// </summary>
    private static double[]? ResolverGauss(double[,] matriz, double[] terminos)
    {
        int m = terminos.Length;
        var a = (double[,])matriz.Clone();
        var b = (double[])terminos.Clone();

        double escala = 0.0;
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
                escala = Math.Max(escala, Math.Abs(a[i, j]));
        if (escala == 0.0) return null;
        double umbral = ToleranciaSingular * escala;

        for (int col = 0; col < m; col++)
        {
            int pivote = col;
            for (int fila = col + 1; fila < m; fila++)
                if (Math.Abs(a[fila, col]) > Math.Abs(a[pivote, col]))
                    pivote = fila;
            if (Math.Abs(a[pivote, col]) < umbral) return null;   // singular → mecanismo

            if (pivote != col)
            {
                for (int j = 0; j < m; j++)
                    (a[col, j], a[pivote, j]) = (a[pivote, j], a[col, j]);
                (b[col], b[pivote]) = (b[pivote], b[col]);
            }

            for (int fila = col + 1; fila < m; fila++)
            {
                double factor = a[fila, col] / a[col, col];
                if (factor == 0.0) continue;
                for (int j = col; j < m; j++)
                    a[fila, j] -= factor * a[col, j];
                b[fila] -= factor * b[col];
            }
        }

        var x = new double[m];
        for (int i = m - 1; i >= 0; i--)
        {
            double suma = b[i];
            for (int j = i + 1; j < m; j++)
                suma -= a[i, j] * x[j];
            x[i] = suma / a[i, i];
        }
        return x;
    }

    // ----- Utilidades geométricas -----

    /// <summary>Ordena y fusiona posiciones coincidentes dentro de <see cref="ToleranciaNodo"/>.</summary>
    private static List<double> Deduplicar(List<double> posiciones)
    {
        posiciones.Sort();
        var nodos = new List<double>(posiciones.Count);
        foreach (var p in posiciones)
            if (nodos.Count == 0 || p - nodos[^1] > ToleranciaNodo)
                nodos.Add(p);
        return nodos;
    }

    /// <summary>Índice del tramo que contiene la abscisa <paramref name="x"/>.</summary>
    private static int IndiceTramoEn(double[] fronteras, double x)
    {
        for (int i = 0; i < fronteras.Length - 1; i++)
            if (x <= fronteras[i + 1] + ToleranciaNodo)
                return i;
        return fronteras.Length - 2;
    }

    /// <summary>Índice del nodo más cercano a la abscisa <paramref name="x"/>.</summary>
    private static int NodoMasCercano(List<double> nodosX, double x)
    {
        int mejor = 0;
        double distancia = Math.Abs(nodosX[0] - x);
        for (int i = 1; i < nodosX.Count; i++)
        {
            double d = Math.Abs(nodosX[i] - x);
            if (d < distancia) { distancia = d; mejor = i; }
        }
        return mejor;
    }

    /// <summary>Intensidad distribuida factorizada (kN/m) sobre un tramo en una combinación.</summary>
    private static double IntensidadDistribuida(TramoViga tramo, CombinacionCarga combo)
    {
        double w = 0.0;
        foreach (var carga in tramo.Cargas)
        {
            if (carga.Tipo != TipoCargaElemento.Distribuida) continue;
            double factor = combo[carga.CodigoCaso];
            if (factor != 0.0) w += factor * carga.Magnitud;
        }
        return w;
    }
}

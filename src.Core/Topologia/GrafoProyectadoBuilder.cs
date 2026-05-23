using System;
using System.Collections.Generic;
using System.Numerics;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;

namespace LosasPlus.Topologia;

/// <summary>
/// Motor estático puro que proyecta el modelo plano de persistencia v3
/// (<see cref="Proyecto"/> → <see cref="Edificio"/> → <see cref="Nivel"/> →
/// colecciones planas de Vigas / Columnas / Zapatas / Muros) sobre el
/// grafo estructural sintético tridimensional descrito por
/// <see cref="IGrafoEstructural"/> (Fase 3D-I2 del Plan Maestro de
/// Expansión 3D).
///
/// <para>
/// <b>Decisiones de proyección</b>:
/// <list type="bullet">
///   <item><b>Z absoluto</b>: la elevación de cada nivel se toma directamente
///   de <see cref="Nivel.Cota"/> (metros sobre el origen del edificio).</item>
///   <item><b>Muros</b>: poseen coordenadas X/Y reales en
///   <see cref="Muro.PuntoInicio"/>/<see cref="Muro.PuntoFin"/>. Se proyecta
///   un rectángulo vertical de 4 nodos (base + corona) y 4 aristas
///   (base, dos verticales, corona) con altura <see cref="Muro.Altura"/>.</item>
///   <item><b>Columnas y Zapatas</b>: el modelo v3 no porta coordenadas X/Y
///   globales para estos elementos. Se asigna una grilla virtual
///   determinista de paso <see cref="EspaciadoGrillaArtificialM"/> indexada
///   por el <see cref="Columna.Id"/> / <see cref="ZapataAislada.Id"/> para
///   que <c>Columna(Id=N)</c> y <c>Zapata(Id=N)</c> compartan exactamente la
///   misma posición en planta — la correlación de Auditoría (Fase 7) los
///   empareja por Id y ahora la geometría también lo refleja.</item>
///   <item><b>Vigas</b>: conectan los nodos corona de dos columnas
///   consecutivas del nivel. Si el nivel tiene menos de dos columnas, la
///   viga se omite del grafo (no se sintetiza una posición artificial
///   sin sentido topológico).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Fusión de nodos coincidentes</b>: todos los puntos se ingresan al
/// <see cref="HashEspacialUniforme"/> con tolerancia de fusión
/// configurable (por defecto 1 mm — default ETABS / SAP2000). Esto
/// garantiza que las cabezas de columna y la base de viga compartan un
/// único <see cref="NodoSintetico"/>.
/// </para>
///
/// <para>
/// <b>Aislamiento</b>: este tipo vive 100 % en <c>src.Core/Topologia</c>.
/// Sin dependencias de WPF, HelixToolkit, DirectX, OxyPlot o cualquier
/// ensamblado gráfico. La capa de presentación (Fase 3D-I1) consume el
/// <see cref="IGrafoEstructural"/> retornado y produce las mallas
/// visuales.
/// </para>
/// </summary>
public static class GrafoProyectadoBuilder
{
    /// <summary>
    /// Paso de la grilla virtual para columnas/zapatas sin coordenadas X/Y
    /// reales en el modelo v3 (6.0 m). Esta separación artificial se
    /// retirará cuando una iteración futura introduzca un campo de posición
    /// 2D al dominio de <see cref="Columna"/> y <see cref="ZapataAislada"/>.
    /// </summary>
    public const double EspaciadoGrillaArtificialM = 6.0;

    /// <summary>Columnas por fila en la grilla virtual antes de saltar a la fila siguiente.</summary>
    public const int ColumnasPorFilaGrillaArtificial = 8;

    /// <summary>
    /// Construye el grafo estructural sintético derivado del proyecto.
    /// Es una operación pura: el <paramref name="proyecto"/> no es mutado
    /// y la instancia retornada es inmutable.
    /// </summary>
    /// <param name="proyecto">Proyecto v3 a proyectar.</param>
    /// <param name="toleranciaMm">
    /// Tolerancia de fusión de nodos coincidentes, en milímetros. Por
    /// defecto 1 mm (default ETABS / SAP2000).
    /// </param>
    /// <returns>Grafo estructural inmutable listo para consumo del visor 3D / exportador SAF.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="proyecto"/> es <c>null</c>.</exception>
    public static IGrafoEstructural Construir(Proyecto proyecto, double toleranciaMm = 1.0)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));

        double toleranciaM = toleranciaMm * 0.001;
        var hash = new HashEspacialUniforme();
        var aristas = new List<AristaSintetica>();
        var incidentesPorNodo = new Dictionary<int, HashSet<DomainKey>>();

        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                double zNivel = nivel.Cota;

                // Mapeo local del nivel: índice secuencial de columna → Id de nodo
                // corona (Z = zNivel + Altura). Lo usan las vigas para anclarse
                // a las cabezas de columna ya emitidas.
                var nodosCoronaColumnaPorIndice = new List<int>();

                ProyectarMuros(nivel, zNivel, toleranciaM, hash, aristas, incidentesPorNodo);
                ProyectarZapatasYColumnas(nivel, zNivel, toleranciaM, hash, aristas,
                                          incidentesPorNodo, nodosCoronaColumnaPorIndice);
                ProyectarVigas(nivel, toleranciaM, hash, aristas, incidentesPorNodo,
                               nodosCoronaColumnaPorIndice);
            }
        }

        return EmpaquetarGrafo(hash, aristas, incidentesPorNodo);
    }

    // ===================================================================
    // 1) MUROS — únicas entidades con coordenadas X/Y reales en v3
    // ===================================================================

    private static void ProyectarMuros(
        Nivel nivel, double zNivel, double toleranciaM,
        HashEspacialUniforme hash,
        List<AristaSintetica> aristas,
        Dictionary<int, HashSet<DomainKey>> incidentesPorNodo)
    {
        foreach (var sistema in nivel.Sistemas)
        {
            foreach (var muro in sistema.Muros)
            {
                double altura = muro.Altura > 0.0 ? muro.Altura : 3.0;
                double zTop = zNivel + altura;

                var p0 = new Vector3((float)muro.PuntoInicio.X, (float)muro.PuntoInicio.Y, (float)zNivel);
                var p1 = new Vector3((float)muro.PuntoFin.X,    (float)muro.PuntoFin.Y,    (float)zNivel);
                var p2 = new Vector3((float)muro.PuntoFin.X,    (float)muro.PuntoFin.Y,    (float)zTop);
                var p3 = new Vector3((float)muro.PuntoInicio.X, (float)muro.PuntoInicio.Y, (float)zTop);

                int id0 = hash.ObtenerOInsertarNodoId(p0, toleranciaM);
                int id1 = hash.ObtenerOInsertarNodoId(p1, toleranciaM);
                int id2 = hash.ObtenerOInsertarNodoId(p2, toleranciaM);
                int id3 = hash.ObtenerOInsertarNodoId(p3, toleranciaM);

                var elem = new DomainKey(TipoElemento.Muro, muro.Id);

                // 4 aristas del rectángulo vertical del eje del muro:
                // base, vertical j, corona, vertical i.
                AgregarArista(id0, id1, elem, hash, aristas, incidentesPorNodo);
                AgregarArista(id1, id2, elem, hash, aristas, incidentesPorNodo);
                AgregarArista(id2, id3, elem, hash, aristas, incidentesPorNodo);
                AgregarArista(id3, id0, elem, hash, aristas, incidentesPorNodo);
            }
        }
    }

    // ===================================================================
    // 2) COLUMNAS + ZAPATAS — pareadas por Id en la grilla artificial
    // ===================================================================

    private static void ProyectarZapatasYColumnas(
        Nivel nivel, double zNivel, double toleranciaM,
        HashEspacialUniforme hash,
        List<AristaSintetica> aristas,
        Dictionary<int, HashSet<DomainKey>> incidentesPorNodo,
        List<int> nodosCoronaColumnaPorIndice)
    {
        // Índice por Id de zapata para emparejarla con la columna del mismo Id.
        // Se usa Dictionary<int, ZapataAislada> en lugar de LINQ por claridad
        // y para evitar dependencia de System.Linq en este caliente del builder.
        var zapatasPorId = new Dictionary<int, ZapataAislada>();
        foreach (var zap in nivel.Zapatas)
        {
            // Cuando hay duplicados (caso edge: dos zapatas con mismo Id) se
            // mantiene la última — el modelo de Auditoría tampoco soporta
            // duplicados, así que el caso se cubre con la regla LIFO.
            zapatasPorId[zap.Id] = zap;
        }

        int idxSecuencialColumna = 0;
        foreach (var columna in nivel.Columnas)
        {
            // Indexación determinista por Id (con fallback para Id <= 0).
            int idGrilla = columna.Id > 0 ? columna.Id : (10_000 + idxSecuencialColumna);
            var (x, y) = PosicionEnGrillaArtificial(idGrilla);

            // Si existe una zapata pareada por Id, sintetizar la zapata primero.
            // Su nodo superior coincide con la base de la columna en (X, Y, zNivel).
            int idNodoBaseColumna;
            if (zapatasPorId.TryGetValue(columna.Id, out var zapataPareada))
            {
                double espesor = zapataPareada.Dimensiones.EspesorH > 0
                    ? zapataPareada.Dimensiones.EspesorH
                    : 0.30;
                double zZapBase = zNivel - espesor;

                var pZapBase = new Vector3((float)x, (float)y, (float)zZapBase);
                var pZapTop  = new Vector3((float)x, (float)y, (float)zNivel);

                int idZapBase = hash.ObtenerOInsertarNodoId(pZapBase, toleranciaM);
                int idZapTop  = hash.ObtenerOInsertarNodoId(pZapTop,  toleranciaM);

                var elemZap = new DomainKey(TipoElemento.Zapata, zapataPareada.Id);
                AgregarArista(idZapBase, idZapTop, elemZap, hash, aristas, incidentesPorNodo);

                // La columna arranca desde el nodo superior de la zapata
                // (consolidación garantizada por el HashEspacialUniforme).
                idNodoBaseColumna = idZapTop;

                // Marcar la zapata como ya procesada (no se vuelve a emitir
                // en el barrido huérfano posterior).
                zapatasPorId.Remove(columna.Id);
            }
            else
            {
                // Sin zapata pareada: la columna se ancla a un nodo nuevo a
                // nivel del piso (Z = zNivel).
                var pBaseCol = new Vector3((float)x, (float)y, (float)zNivel);
                idNodoBaseColumna = hash.ObtenerOInsertarNodoId(pBaseCol, toleranciaM);
            }

            // Cuerpo de la columna: del nodo base al nodo corona.
            double alturaColumna = columna.Altura > 0.0 ? columna.Altura : 3.0;
            var pCorona = new Vector3((float)x, (float)y, (float)(zNivel + alturaColumna));
            int idCorona = hash.ObtenerOInsertarNodoId(pCorona, toleranciaM);

            var elemCol = new DomainKey(TipoElemento.Columna, columna.Id);
            AgregarArista(idNodoBaseColumna, idCorona, elemCol, hash, aristas, incidentesPorNodo);

            // Registrar el nodo corona para que las vigas del nivel lo referencien.
            nodosCoronaColumnaPorIndice.Add(idCorona);
            idxSecuencialColumna++;
        }

        // Barrido huérfano: zapatas sin columna pareada. Se renderizan en su
        // posición de grilla determinista para que el ingeniero las vea aun
        // sin la columna correspondiente.
        int idxSecuencialZapata = 0;
        foreach (var zapHuerfana in zapatasPorId.Values)
        {
            int idGrilla = zapHuerfana.Id > 0 ? zapHuerfana.Id : (20_000 + idxSecuencialZapata);
            var (x, y) = PosicionEnGrillaArtificial(idGrilla);

            double espesor = zapHuerfana.Dimensiones.EspesorH > 0
                ? zapHuerfana.Dimensiones.EspesorH
                : 0.30;
            double zZapBase = zNivel - espesor;

            var pZapBase = new Vector3((float)x, (float)y, (float)zZapBase);
            var pZapTop  = new Vector3((float)x, (float)y, (float)zNivel);

            int idZapBase = hash.ObtenerOInsertarNodoId(pZapBase, toleranciaM);
            int idZapTop  = hash.ObtenerOInsertarNodoId(pZapTop,  toleranciaM);

            var elemZap = new DomainKey(TipoElemento.Zapata, zapHuerfana.Id);
            AgregarArista(idZapBase, idZapTop, elemZap, hash, aristas, incidentesPorNodo);
            idxSecuencialZapata++;
        }
    }

    // ===================================================================
    // 3) VIGAS — conectan nodos corona de columnas consecutivas
    // ===================================================================

    private static void ProyectarVigas(
        Nivel nivel, double toleranciaM,
        HashEspacialUniforme hash,
        List<AristaSintetica> aristas,
        Dictionary<int, HashSet<DomainKey>> incidentesPorNodo,
        IReadOnlyList<int> nodosCoronaColumnaPorIndice)
    {
        int n = nodosCoronaColumnaPorIndice.Count;
        if (n < 2) return;   // sin al menos 2 columnas no hay nodos para unir

        int idxViga = 0;
        foreach (var viga in nivel.Vigas)
        {
            // Conecta cíclicamente la viga(i) con las columnas (i % n) → ((i+1) % n).
            int idA = nodosCoronaColumnaPorIndice[idxViga % n];
            int idB = nodosCoronaColumnaPorIndice[(idxViga + 1) % n];
            idxViga++;

            if (idA == idB) continue;   // colapso degenerado (sólo 1 columna real)

            var elem = new DomainKey(TipoElemento.Viga, viga.Id);
            AgregarArista(idA, idB, elem, hash, aristas, incidentesPorNodo);
        }
    }

    // ===================================================================
    // HELPERS
    // ===================================================================

    /// <summary>
    /// Distribuye un Id (entero positivo) en la grilla virtual artificial
    /// de paso <see cref="EspaciadoGrillaArtificialM"/> con
    /// <see cref="ColumnasPorFilaGrillaArtificial"/> columnas por fila.
    /// La función es <b>determinista</b>: <c>Posicion(Id=N)</c> siempre
    /// devuelve el mismo (X, Y), lo que permite a Columna(Id=N) y
    /// Zapata(Id=N) compartir su nodo en planta.
    /// </summary>
    public static (double X, double Y) PosicionEnGrillaArtificial(int idGrilla)
    {
        if (idGrilla < 0) idGrilla = 0;
        // Se desplaza Id en -1 para que el Id=1 (caso típico inicial)
        // aterrice en (0, 0).
        int desplazado = Math.Max(0, idGrilla - 1);
        int fila = desplazado / ColumnasPorFilaGrillaArtificial;
        int col  = desplazado % ColumnasPorFilaGrillaArtificial;
        return (col * EspaciadoGrillaArtificialM, fila * EspaciadoGrillaArtificialM);
    }

    /// <summary>
    /// Agrega una arista con cálculo de ejes locales CSI y registra la
    /// incidencia del elemento en ambos nodos. Filtra aristas degeneradas
    /// (nodos idénticos o casi coincidentes tras la fusión).
    /// </summary>
    private static void AgregarArista(
        int idInicio, int idFin, DomainKey elemento,
        HashEspacialUniforme hash,
        List<AristaSintetica> aristas,
        Dictionary<int, HashSet<DomainKey>> incidentesPorNodo)
    {
        if (idInicio == idFin) return;   // arista degenerada

        var posI = hash.PosicionPorId(idInicio);
        var posJ = hash.PosicionPorId(idFin);
        if ((posJ - posI).LengthSquared() < 1e-12f) return;   // longitud nula

        var ejes = EjesLocalesCSI.Calcular(posI, posJ, anguloRollGrados: 0.0);
        aristas.Add(new AristaSintetica(idInicio, idFin, elemento, ejes));

        RegistrarIncidencia(incidentesPorNodo, idInicio, elemento);
        RegistrarIncidencia(incidentesPorNodo, idFin,    elemento);
    }

    private static void RegistrarIncidencia(
        Dictionary<int, HashSet<DomainKey>> incidentesPorNodo,
        int idNodo, DomainKey elemento)
    {
        if (!incidentesPorNodo.TryGetValue(idNodo, out var set))
        {
            set = new HashSet<DomainKey>();
            incidentesPorNodo[idNodo] = set;
        }
        set.Add(elemento);
    }

    /// <summary>
    /// Materializa la lista final de <see cref="NodoSintetico"/>s
    /// rellenando los <c>ElementosIncidentes</c> de cada uno, y empaqueta
    /// el <see cref="GrafoEstructural"/> inmutable.
    /// </summary>
    private static IGrafoEstructural EmpaquetarGrafo(
        HashEspacialUniforme hash,
        List<AristaSintetica> aristas,
        Dictionary<int, HashSet<DomainKey>> incidentesPorNodo)
    {
        var nodos = new List<NodoSintetico>(hash.Cuenta);
        for (int id = 0; id < hash.Cuenta; id++)
        {
            IReadOnlyList<DomainKey> incidentes;
            if (incidentesPorNodo.TryGetValue(id, out var set) && set.Count > 0)
            {
                var lista = new List<DomainKey>(set.Count);
                foreach (var k in set) lista.Add(k);
                incidentes = lista;
            }
            else
            {
                incidentes = Array.Empty<DomainKey>();
            }
            nodos.Add(new NodoSintetico(id, hash.PosicionPorId(id), incidentes));
        }

        return new GrafoEstructural(nodos, aristas);
    }
}

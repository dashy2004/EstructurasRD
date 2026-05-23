using System.Collections.Generic;
using System.Numerics;

namespace LosasPlus.Topologia;

/// <summary>
/// Estructura de aceleración para fusión de nodos coincidentes en el grafo
/// estructural sintético (Fase 3D-I2 del Plan Maestro). Discretiza el
/// espacio 3D en celdas cúbicas uniformes y mantiene un
/// <see cref="Dictionary{TKey, TValue}"/> que mapea
/// <c>(celdaX, celdaY, celdaZ)</c> a las listas de IDs de nodo
/// registrados en esa celda.
///
/// <para>
/// <b>Algoritmo</b>: al buscar la posición de un nuevo punto, se
/// discretiza su coordenada (X, Y, Z) en la celda canónica y se inspeccionan
/// las 27 celdas del vecindario 3×3×3 (la propia + las 26 adyacentes). Si
/// algún nodo cae a distancia euclídea ≤ <c>toleranciaM</c>, se devuelve
/// su Id; en caso contrario se crea un nodo nuevo y se registra en su
/// celda canónica. Complejidad amortizada O(1) por inserción/búsqueda
/// asumiendo un número constante de nodos por celda.
/// </para>
///
/// <para>
/// <b>Tamaño de celda</b>: por defecto 10 mm, suficientemente más grande
/// que la tolerancia de 1 mm para que el vecindario 27-celdas garantice
/// la captura del nodo más cercano. Reducir la celda perjudica el conteo
/// del vecindario; agrandarla degrada la complejidad amortizada.
/// </para>
///
/// <para>
/// Es un tipo <b>puro de dominio</b>: vive sólo en
/// <c>src.Core/Topologia</c> y no referencia tipos gráficos.
/// </para>
/// </summary>
public sealed class HashEspacialUniforme
{
    /// <summary>Tamaño de celda por defecto, en metros (10 mm).</summary>
    public const double TamanoCeldaPredeterminadoM = 0.010;

    /// <summary>Tolerancia de fusión por defecto, en metros (1 mm — default ETABS / SAP2000).</summary>
    public const double ToleranciaPredeterminadaM = 0.001;

    private readonly double _tamanoCeldaM;
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _celdas = new();
    private readonly List<Vector3> _posicionesPorId = new();

    /// <summary>
    /// Construye un hash espacial con el tamaño de celda indicado. La
    /// elección de la celda debe ser ≥ 2× la tolerancia de fusión
    /// esperada para que el vecindario 27-celdas capture todos los
    /// candidatos.
    /// </summary>
    /// <param name="tamanoCeldaM">
    /// Tamaño de celda en metros. Debe ser positivo; por defecto 10 mm.
    /// </param>
    public HashEspacialUniforme(double tamanoCeldaM = TamanoCeldaPredeterminadoM)
    {
        if (tamanoCeldaM <= 0.0)
            throw new System.ArgumentOutOfRangeException(
                nameof(tamanoCeldaM), tamanoCeldaM,
                "El tamaño de celda debe ser positivo.");
        _tamanoCeldaM = tamanoCeldaM;
    }

    /// <summary>Cantidad de nodos registrados hasta el momento.</summary>
    public int Cuenta => _posicionesPorId.Count;

    /// <summary>
    /// Devuelve la posición de un nodo previamente registrado por
    /// <see cref="ObtenerOInsertarNodoId"/> a partir de su Id.
    /// </summary>
    public Vector3 PosicionPorId(int id) => _posicionesPorId[id];

    /// <summary>
    /// Busca un nodo dentro de la tolerancia indicada alrededor de
    /// <paramref name="posicion"/>. Si existe, devuelve su Id. Si no,
    /// crea un nuevo nodo, lo registra en su celda canónica y devuelve
    /// el Id recién emitido.
    /// </summary>
    /// <param name="posicion">Posición 3D del nodo candidato (m).</param>
    /// <param name="toleranciaM">
    /// Tolerancia euclídea de fusión, en metros. Por defecto 1 mm
    /// (default ETABS / SAP2000).
    /// </param>
    /// <returns>
    /// Id estable del nodo (existente o recién creado) — los Ids son
    /// monotónicos crecientes desde 0.
    /// </returns>
    public int ObtenerOInsertarNodoId(Vector3 posicion, double toleranciaM = ToleranciaPredeterminadaM)
    {
        var celda = CalcularCelda(posicion);
        float toleranciaCuadrada = (float)(toleranciaM * toleranciaM);

        // Inspeccionar las 27 celdas del vecindario 3×3×3.
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            var vecina = (celda.X + dx, celda.Y + dy, celda.Z + dz);
            if (!_celdas.TryGetValue(vecina, out var nodosEnCelda)) continue;
            foreach (int idCandidato in nodosEnCelda)
            {
                var diferencia = _posicionesPorId[idCandidato] - posicion;
                if (diferencia.LengthSquared() <= toleranciaCuadrada)
                    return idCandidato;
            }
        }

        // No hay nodo cercano: crear uno nuevo.
        int idNuevo = _posicionesPorId.Count;
        _posicionesPorId.Add(posicion);
        if (!_celdas.TryGetValue(celda, out var lista))
        {
            lista = new List<int>(2);
            _celdas[celda] = lista;
        }
        lista.Add(idNuevo);
        return idNuevo;
    }

    /// <summary>
    /// Discretiza una posición 3D en el índice de celda canónica
    /// <c>(floor(X / s), floor(Y / s), floor(Z / s))</c>.
    /// </summary>
    private (int X, int Y, int Z) CalcularCelda(Vector3 posicion)
    {
        int x = (int)System.Math.Floor(posicion.X / _tamanoCeldaM);
        int y = (int)System.Math.Floor(posicion.Y / _tamanoCeldaM);
        int z = (int)System.Math.Floor(posicion.Z / _tamanoCeldaM);
        return (x, y, z);
    }
}

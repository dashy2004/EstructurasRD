using System.Collections.Generic;
using System.Numerics;

namespace LosasPlus.Topologia;

/// <summary>
/// Nodo del grafo estructural sintético — un punto del espacio 3D
/// compartido por uno o más miembros (Fase 3D-I2 del Plan Maestro).
/// Es un objeto de vista derivada: vive sólo en memoria, no se persiste al
/// JSON v3 del proyecto.
///
/// <para>
/// La fusión de nodos coincidentes (tolerancia 1 mm por defecto) la realiza
/// <see cref="GrafoProyectadoBuilder"/> apoyado en
/// <see cref="HashEspacialUniforme"/>. Una vez fusionados, los miembros
/// que comparten un nodo lo apuntan por <see cref="Id"/>.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF / HelixToolkit.</para>
/// </summary>
/// <param name="Id">
/// Identificador entero del nodo dentro del grafo. Asignado de forma
/// determinista por el <see cref="HashEspacialUniforme"/> al insertar.
/// </param>
/// <param name="Posicion">
/// Coordenadas absolutas del nodo en metros (X, Y, Z global del proyecto).
/// </param>
/// <param name="ElementosIncidentes">
/// Claves de los elementos del dominio (Viga / Columna / Zapata / Muro)
/// que comparten este nodo en su frontera. Lista de sólo lectura;
/// permite al consumidor (visor 3D, exportador SAF, futuro solver)
/// resolver la conectividad del grafo sin guardar referencias directas
/// al modelo.
/// </param>
public sealed record NodoSintetico(
    int Id,
    Vector3 Posicion,
    IReadOnlyList<DomainKey> ElementosIncidentes);

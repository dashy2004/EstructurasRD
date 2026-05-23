using System;
using System.Collections.Generic;

namespace LosasPlus.Topologia;

/// <summary>
/// Vista derivada del proyecto v3 como grafo estructural: una colección
/// inmutable de <see cref="NodoSintetico"/>s 3D y las
/// <see cref="AristaSintetica"/>s que los conectan (Fase 3D-I2 del Plan
/// Maestro de Expansión 3D). El grafo es <b>read-only</b>: el cliente
/// (visor 3D, exportador SAF, futuro solver) lo consume pero no muta el
/// modelo original.
///
/// <para>
/// Se produce vía <see cref="GrafoProyectadoBuilder.Construir(Models.Proyecto, double)"/>.
/// La instancia es válida sólo hasta la siguiente mutación del proyecto;
/// el consumidor decide si la cachea y la invalida con eventos
/// <c>PropertyChanged</c> / <c>CollectionChanged</c> o si la reconstruye
/// en cada acceso.
/// </para>
/// </summary>
public interface IGrafoEstructural
{
    /// <summary>Nodos del grafo (puntos 3D compartidos por miembros).</summary>
    IReadOnlyList<NodoSintetico> Nodos { get; }

    /// <summary>
    /// Aristas del grafo (segmentos i → j entre nodos). Cada arista
    /// referencia el elemento de dominio que la generó vía
    /// <see cref="AristaSintetica.Elemento"/>.
    /// </summary>
    IReadOnlyList<AristaSintetica> Aristas { get; }
}

/// <summary>
/// Implementación canónica de <see cref="IGrafoEstructural"/> como record
/// inmutable. Se reutiliza en tests y mocks; el builder devuelve
/// instancias de esta clase.
/// </summary>
/// <param name="Nodos">Lista inmutable de nodos sintéticos.</param>
/// <param name="Aristas">Lista inmutable de aristas sintéticas.</param>
public sealed record GrafoEstructural(
    IReadOnlyList<NodoSintetico> Nodos,
    IReadOnlyList<AristaSintetica> Aristas) : IGrafoEstructural
{
    /// <summary>
    /// Grafo vacío reutilizable — para proyectos sin elementos o estados
    /// de transición (cargando, error, etc.).
    /// </summary>
    public static IGrafoEstructural Vacio { get; } = new GrafoEstructural(
        Array.Empty<NodoSintetico>(),
        Array.Empty<AristaSintetica>());
}

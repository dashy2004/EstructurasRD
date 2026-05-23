namespace LosasPlus.Topologia;

/// <summary>
/// Categoría del elemento estructural detrás de cada nodo o arista del grafo
/// sintético (Fase 3D-I2 del Plan Maestro de Expansión 3D). Cada categoría
/// corresponde a una colección reactiva del <c>Nivel</c> en el modelo v3.
/// </summary>
public enum TipoElemento
{
    /// <summary>Viga continua del nivel (<c>Nivel.Vigas</c>).</summary>
    Viga,
    /// <summary>Columna RC del nivel (<c>Nivel.Columnas</c>).</summary>
    Columna,
    /// <summary>Zapata aislada del nivel (<c>Nivel.Zapatas</c>).</summary>
    Zapata,
    /// <summary>Muro estructural CAD (<c>Sistema.Muros</c>).</summary>
    Muro,
}

/// <summary>
/// Identificador inmutable de un elemento del dominio v3 — tipo + Id entero.
/// Sirve como llave estable para mapear un <see cref="NodoSintetico"/> o una
/// <see cref="AristaSintetica"/> del grafo proyectado de vuelta al objeto
/// original (Viga / Columna / Zapata / Muro) sin sostener referencias
/// directas a las instancias del dominio.
///
/// <para>
/// El tipo es <c>readonly record struct</c> para asegurar:
/// <list type="bullet">
///   <item>Igualdad por valor (Tipo + Id) — adecuada para uso como key de
///   <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>.</item>
///   <item>Cero allocations al pasarlo entre el motor y la capa de
///   presentación (Helix Toolkit, exportador SAF, etc.).</item>
///   <item>Inmutabilidad — un elemento ya identificado no puede mutarse.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Tipo">Categoría del elemento estructural.</param>
/// <param name="Id">Identificador entero del elemento dentro de su colección.</param>
public readonly record struct DomainKey(TipoElemento Tipo, int Id);

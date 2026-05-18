namespace LosasPlus.Models.Cad;

/// <summary>
/// Texto — traducción de las entidades DXF <c>TEXT</c> y <c>MTEXT</c>.
/// La posición está en metros; la <see cref="Altura"/> también.
///
/// <para>
/// En planos de arquitectura los textos suelen ser rótulos de ambiente
/// ("SALA", "COCINA", "DORMITORIO 1") — útiles para nombrar las losas
/// generadas en la Fase 2.
/// </para>
/// </summary>
public sealed class TextoCad : EntidadCad
{
    /// <summary>Punto de inserción del texto (m).</summary>
    public PuntoCad Posicion { get; init; }

    /// <summary>Contenido textual.</summary>
    public string Contenido { get; init; } = "";

    /// <summary>Altura de la fuente (m).</summary>
    public double Altura { get; init; }

    /// <summary>Rotación del texto en grados (0 = horizontal).</summary>
    public double RotacionGrados { get; init; }

    public override TipoEntidadCad Tipo => TipoEntidadCad.Texto;
}

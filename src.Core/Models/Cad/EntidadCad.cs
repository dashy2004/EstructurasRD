namespace LosasPlus.Models.Cad;

/// <summary>Discriminador del tipo concreto de una <see cref="EntidadCad"/>.</summary>
public enum TipoEntidadCad
{
    Linea,
    Polilinea,
    Texto,
    Arco,
}

/// <summary>
/// Base abstracta de toda entidad geométrica importada de un archivo <c>.DXF</c>.
///
/// <para>
/// Las clases derivadas (<see cref="LineaCad"/>, <see cref="PolilineaCad"/>,
/// <see cref="TextoCad"/>, <see cref="ArcoCad"/>) son <b>modelos puros de
/// dominio</b>: viven en <c>src.Core</c>, usan coordenadas <see cref="PuntoCad"/>
/// en metros y <b>no referencian ningún tipo de WPF</b>. La conversión a
/// <c>Geometry</c>/<c>Point</c> ocurre exclusivamente en la capa de presentación.
/// </para>
/// </summary>
public abstract class EntidadCad
{
    /// <summary>Nombre de la capa (layer) del DXF de origen. Default <c>"0"</c> (capa por defecto de AutoCAD).</summary>
    public string Capa { get; init; } = "0";

    /// <summary>Tipo concreto de la entidad — permite despachar sin reflexión ni <c>is</c>.</summary>
    public abstract TipoEntidadCad Tipo { get; }
}

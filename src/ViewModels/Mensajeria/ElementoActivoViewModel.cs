using LosasPlus.Columnas;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;

namespace LosasPlus.ViewModels.Mensajeria;

/// <summary>
/// Adaptador read-only que proyecta una entidad de dominio
/// (<see cref="Columna"/> / <see cref="Viga"/> / <see cref="ZapataAislada"/>)
/// a un DTO inmutable apto para binding XAML (Liga B paso B2).
///
/// <para>
/// <b>Garantía de aislamiento</b>: la UI no posee referencias mutables al
/// dominio. Cada cambio de selección crea una nueva instancia del adapter
/// vía las factories <c>Adaptar(...)</c>; los setters mutables del dominio
/// nunca son alcanzables desde el binding tree. Cualquier mutación al
/// modelo subyacente debe pasar por los comandos transaccionales del
/// <c>MainViewModel</c>.
/// </para>
///
/// <para>
/// <b>Null Object</b>: el campo estático <see cref="Vacio"/> es la
/// instancia compartida que se publica cuando la selección está vacía,
/// la entidad fue eliminada concurrentemente, la <see cref="Topologia.DomainKey"/>
/// es huérfana, o un error en cascada degrada la resolución. Evita
/// <c>NullReferenceException</c> en el binding tree y mantiene el panel
/// con un placeholder visible en lugar de quedar en blanco.
/// </para>
///
/// <para>
/// <b>Inmutabilidad</b>: las propiedades son <c>get</c>-only; no implementa
/// <see cref="System.ComponentModel.INotifyPropertyChanged"/> porque las
/// instancias son inmutables. La notificación al binding tree se produce
/// vía el <c>OnPropertyChanged</c> de la propiedad
/// <c>MainViewModel.ElementoActivo</c>, que reemplaza la referencia
/// completa al adapter en cada selección.
/// </para>
/// </summary>
public sealed class ElementoActivoViewModel
{
    /// <summary>
    /// Null Object compartido — instancia singleton para selección vacía
    /// o estado degradado. <see cref="EsVacio"/> es <c>true</c>; el panel
    /// XAML aplica reducción de opacidad y muestra el placeholder
    /// <see cref="Nombre"/>.
    /// </summary>
    public static readonly ElementoActivoViewModel Vacio = new(
        tipo: "—",
        id: 0,
        nombre: "Sin selección",
        dimensiones: string.Empty,
        ratioDC: 0.0,
        esVacio: true);

    /// <summary>Etiqueta del tipo estructural — "Viga", "Columna", "Zapata", "—".</summary>
    public string Tipo { get; }

    /// <summary>Identificador del elemento dentro de su nivel.</summary>
    public int Id { get; }

    /// <summary>Nombre descriptivo proporcionado por el dominio (p. ej. "C-1").</summary>
    public string Nombre { get; }

    /// <summary>
    /// Dimensiones formateadas como string mono-font legible (p. ej.
    /// <c>"30×30 cm · H=3.00 m"</c>). Sin acceso a las propiedades
    /// numéricas crudas — la UI sólo ve el texto resultante.
    /// </summary>
    public string Dimensiones { get; }

    /// <summary>
    /// Ratio Demanda/Capacidad del elemento — placeholder en <c>0.0</c>
    /// hasta que Liga B paso B4 conecte la fuente real
    /// (<c>RatioCalculator</c>). El panel XAML expone la celda con
    /// styling por umbral para que B4 sólo cambie el origen del binding.
    /// </summary>
    public double RatioDC { get; }

    /// <summary>
    /// <c>true</c> si la instancia es el Null Object compartido; el panel
    /// XAML lo usa para reducir opacidad y ocultar la métrica D/C.
    /// </summary>
    public bool EsVacio { get; }

    private ElementoActivoViewModel(
        string tipo,
        int id,
        string nombre,
        string dimensiones,
        double ratioDC,
        bool esVacio)
    {
        Tipo        = tipo;
        Id          = id;
        Nombre      = nombre;
        Dimensiones = dimensiones;
        RatioDC     = ratioDC;
        EsVacio     = esVacio;
    }

    /// <summary>
    /// Proyecta una <see cref="Columna"/> a un DTO inmutable. La columna
    /// original no queda referenciada por la instancia retornada.
    /// </summary>
    public static ElementoActivoViewModel Adaptar(Columna c)
    {
        if (c is null) return Vacio;
        string nombre = string.IsNullOrWhiteSpace(c.Nombre) ? $"C-{c.Id}" : c.Nombre;
        string dim    = $"{c.Geometria.Base * 100:F0}×{c.Geometria.Peralte * 100:F0} cm · H={c.Altura:F2} m";
        return new ElementoActivoViewModel("Columna", c.Id, nombre, dim, ratioDC: 0.0, esVacio: false);
    }

    /// <summary>
    /// Proyecta una <see cref="Viga"/> a un DTO inmutable. Las dimensiones
    /// se calculan a partir del primer tramo (B × H) y la longitud total
    /// (suma de tramos). Si la viga no tiene tramos sólo se reporta
    /// <c>L=0.00 m</c>.
    /// </summary>
    public static ElementoActivoViewModel Adaptar(Viga v)
    {
        if (v is null) return Vacio;
        string nombre = string.IsNullOrWhiteSpace(v.Nombre) ? $"V-{v.Id}" : v.Nombre;

        double lTotal = 0.0;
        TramoViga? primer = null;
        foreach (var t in v.Tramos)
        {
            primer ??= t;
            lTotal += t.Longitud;
        }
        string dim = primer is null
            ? $"L={lTotal:F2} m"
            : $"{primer.Base * 1000:F0}×{primer.Peralte * 1000:F0} mm · L={lTotal:F2} m";

        return new ElementoActivoViewModel("Viga", v.Id, nombre, dim, ratioDC: 0.0, esVacio: false);
    }

    /// <summary>
    /// Proyecta una <see cref="ZapataAislada"/> a un DTO inmutable. Las
    /// dimensiones leen <c>Dimensiones.LargoB</c>, <c>AnchoL</c> y
    /// <c>EspesorH</c>.
    /// </summary>
    public static ElementoActivoViewModel Adaptar(ZapataAislada z)
    {
        if (z is null) return Vacio;
        string nombre = string.IsNullOrWhiteSpace(z.Nombre) ? $"Z-{z.Id}" : z.Nombre;
        string dim    = $"{z.Dimensiones.LargoB * 100:F0}×{z.Dimensiones.AnchoL * 100:F0} cm · h={z.Dimensiones.EspesorH * 100:F0} cm";
        return new ElementoActivoViewModel("Zapata", z.Id, nombre, dim, ratioDC: 0.0, esVacio: false);
    }
}

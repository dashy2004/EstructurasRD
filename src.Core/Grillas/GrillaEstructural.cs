using System.Collections.Generic;

namespace LosasPlus.Grillas;

/// <summary>
/// Sistema de ejes estructurales (grilla) de un <c>Edificio</c>:
/// dos colecciones ordenadas de <see cref="EjeNombrado"/>, una por
/// cada dirección del plano horizontal — ejes X (típicamente letras
/// A, B, C) y ejes Y (típicamente números 1, 2, 3). Las
/// intersecciones de estos ejes definen los puntos magnéticos a los
/// que el snap del visor 3D atrae el cursor del usuario en las
/// herramientas de autoría (Módulo 2 Fase 3D-II).
///
/// <para>
/// Tipo <b>puro de dominio</b> — sin dependencias de WPF/Helix. Las
/// listas son <see cref="List{T}"/> simples (no ObservableCollection)
/// porque la mutación interactiva desde un editor lateral (DataGrid
/// CRUD) se difiere al Módulo 2.5; M2 sólo necesita serialización +
/// snap + render.
/// </para>
///
/// <para>
/// <c>record struct</c> mutable: la igualdad estructural es útil para
/// tests de roundtrip JSON, y la mutabilidad de los <c>List</c>
/// internos permite que el deserializer de System.Text.Json los rellene
/// vía <c>JsonObjectCreationHandling.Populate</c>.
/// </para>
/// </summary>
/// <param name="EjesX">Ejes a lo largo de la dirección X (letras), ordenados ascendentemente por posición.</param>
/// <param name="EjesY">Ejes a lo largo de la dirección Y (números), ordenados ascendentemente por posición.</param>
public record struct GrillaEstructural(List<EjeNombrado> EjesX, List<EjeNombrado> EjesY)
{
    /// <summary>
    /// Construye una grilla regular default de 3×3 ejes a paso de 6 m:
    /// A=0, B=6, C=12 (eje X) y 1=0, 2=6, 3=12 (eje Y). Esta es la
    /// grilla que el <c>ProyectoSerializer</c> inyecta automáticamente
    /// al cargar archivos v3 antiguos (sin grilla) para mantener
    /// compatibilidad retroactiva.
    /// </summary>
    public static GrillaEstructural Default() => new(
        EjesX: new List<EjeNombrado>
        {
            new("A",  0.0),
            new("B",  6.0),
            new("C", 12.0),
        },
        EjesY: new List<EjeNombrado>
        {
            new("1",  0.0),
            new("2",  6.0),
            new("3", 12.0),
        });

    /// <summary>
    /// <c>true</c> si la grilla no tiene ningún eje declarado (ambas
    /// listas vacías). Detector usado por el salvavidas del serializer
    /// para decidir si inyecta <see cref="Default"/>.
    /// </summary>
    public readonly bool EstaVacia => EjesX.Count == 0 && EjesY.Count == 0;
}

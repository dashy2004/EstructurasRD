namespace LosasPlus.Topologia;

/// <summary>
/// Arista del grafo estructural sintético — el segmento que une dos
/// <see cref="NodoSintetico"/>s y representa un miembro lineal del modelo
/// (viga, columna o cualquier "edge" extruido como segmento) o una de las
/// aristas constitutivas de un elemento extruido (las 4 verticales de un
/// muro, las 12 aristas de una zapata, etc.). Fase 3D-I2 del Plan Maestro.
///
/// <para>
/// Los identificadores <see cref="IdInicio"/> e <see cref="IdFin"/>
/// referencian los <see cref="NodoSintetico.Id"/> dentro del mismo grafo.
/// El <see cref="EjesLocalesCSI"/> almacena el triedro local del miembro
/// (eje longitudinal i → j + dos ejes transversales). Para aristas
/// "auxiliares" de la extrusión de muros y zapatas el triedro es el
/// del miembro padre.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF / HelixToolkit.</para>
/// </summary>
/// <param name="IdInicio">Id del <see cref="NodoSintetico"/> inicial.</param>
/// <param name="IdFin">Id del <see cref="NodoSintetico"/> final.</param>
/// <param name="Elemento">Llave del elemento del dominio que produjo esta arista.</param>
/// <param name="Ejes">Triedro local CSI a lo largo del segmento.</param>
public sealed record AristaSintetica(
    int IdInicio,
    int IdFin,
    DomainKey Elemento,
    EjesLocalesCSI Ejes);

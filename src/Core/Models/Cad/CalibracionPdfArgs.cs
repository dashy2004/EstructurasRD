namespace LosasPlus.Models.Cad;

/// <summary>
/// DTO inmutable que transporta el resultado de un gesto de calibración del
/// PDF Underlay (Iteración 5 Epic v1.2): el primer punto de referencia
/// (pivote), la distancia geométrica medida en el lienzo y la distancia
/// real introducida por el usuario.
///
/// <para>
/// Lo construye el code-behind del editor flotante de <c>CadView</c> al
/// presionar OK/Enter, y lo recibe <c>AplicarCalibrarPdfCommand</c> en el
/// <c>CadEditorViewModel</c>. El ViewModel calcula el factor de homotecia
/// (<c>DistanciaReal / DistanciaActual</c>) y lo aplica a la
/// <see cref="PdfReferencia"/> conservando el pivote <c>(PivoteX, PivoteY)</c>
/// fijo en el lienzo.
/// </para>
/// </summary>
/// <param name="PivoteX">
/// Coordenada X del primer punto de referencia (P₁), en metros del dominio
/// del lienzo (pre-transform px ÷ PxPorMetro).
/// </param>
/// <param name="PivoteY">
/// Coordenada Y del primer punto de referencia (P₁), en metros del dominio.
/// </param>
/// <param name="DistanciaActual">
/// Distancia geométrica entre P₁ y P₂ medida directamente en el lienzo, en
/// metros del dominio. Es la magnitud "actual" antes de la corrección.
/// </param>
/// <param name="DistanciaReal">
/// Distancia real que el usuario introduce en el editor flotante, en metros
/// físicos del plano arquitectónico. El factor de escala es
/// <c>DistanciaReal / DistanciaActual</c>.
/// </param>
public sealed record CalibracionPdfArgs(
    double PivoteX,
    double PivoteY,
    double DistanciaActual,
    double DistanciaReal);

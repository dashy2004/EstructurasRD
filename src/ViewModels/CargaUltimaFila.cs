namespace LosasPlus.ViewModels;

/// <summary>
/// Fila de desglose de carga última (Wu) de una losa, lista para mostrarse en
/// <see cref="LosasPlus.Views.CargaUltimaWindow"/>. <c>Qmamp</c> es el peso
/// total de los muros del sistema (toneladas, constante por sistema) y
/// <c>Qmap</c> el reparto de esa mampostería sobre el área (t/m², también
/// constante por sistema); <c>Qd</c>, <c>Ql</c> y <c>Qu</c> son cargas
/// superficiales en t/m² (Qd y Qu varían por el espesor de cada losa). La
/// combinación última que produce <c>Qu</c> la fija el proyecto
/// (<c>CargasGlobales.Factores</c>), por defecto 1.2·Qd + 1.6·Ql (ACI 318-05).
/// </summary>
public sealed record CargaUltimaFila(
    string Nivel,
    string Sistema,
    int Losa,
    double Espesor,
    double Qmamp,
    double Qmap,
    double Qd,
    double Ql,
    double Qu);

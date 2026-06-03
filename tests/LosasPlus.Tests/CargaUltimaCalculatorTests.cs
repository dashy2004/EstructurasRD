using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la carga última directa (Sesión D): puente de la geometría real de
/// muros (<see cref="Muro"/>) al peso de mampostería, y composición con el
/// pipeline existente de <c>CalculoEngine</c> (Qmap → Qd → Ql → Qu, LRFD).
/// </summary>
public class CargaUltimaCalculatorTests
{
    private static Muro Mur(double largo, double espesor, double altura)
        => new()
        {
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(largo, 0),
            Espesor = espesor,
            Altura = altura,
        };

    [Fact]
    public void PesoMamposteria_un_muro_usa_densidad_1_8_por_volumen()
    {
        // 1.8 t/m³ · (4 m · 0.15 m · 2.8 m) = 3.024 ton
        var muros = new List<Muro> { Mur(4.0, 0.15, 2.8) };

        var peso = CargaUltimaCalculator.PesoMamposteria(muros);

        Assert.Equal(3.024, peso, 6);
    }
}

using System.Collections.Generic;
using LosasPlus.Calculo;
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

    [Fact]
    public void PesoMamposteria_suma_todos_los_muros()
    {
        var muros = new List<Muro>
        {
            Mur(4.0, 0.15, 2.8),   // 3.024
            Mur(5.0, 0.20, 2.8),   // 1.8·5·0.20·2.8 = 5.04
        };

        var peso = CargaUltimaCalculator.PesoMamposteria(muros);

        Assert.Equal(3.024 + 5.04, peso, 6);
    }

    [Fact]
    public void PesoMamposteria_desde_sistema_usa_su_coleccion_de_muros()
    {
        var sistema = new Sistema();
        sistema.Muros.Add(Mur(4.0, 0.15, 2.8));   // 3.024

        var peso = CargaUltimaCalculator.PesoMamposteria(sistema);

        Assert.Equal(3.024, peso, 6);
    }

    [Fact]
    public void Calcular_compone_muros_con_el_pipeline_de_carga_ultima()
    {
        var cargas = CargasGlobales.SemillaPorDefecto();
        var sistema = new Sistema { Uso = SistemaUso.Entrepiso };
        sistema.Muros.Add(Mur(5.0, 0.15, 2.8));   // Qmamp = 1.8·5·0.15·2.8 = 3.78 ton
        const double hEq = 0.15;
        const double area = 20.0;                 // losa 4×5

        var r = CargaUltimaCalculator.Calcular(sistema, cargas, hEq, area);

        // El orquestador debe encadenar exactamente el pipeline existente:
        var qmamp = 1.8 * 5.0 * 0.15 * 2.8;
        var qmap = CalculoEngine.ComputeQmap(qmamp, area);
        var qd = CalculoEngine.ComputeQd(hEq, cargas, SistemaUso.Entrepiso, qmap);
        var ql = CalculoEngine.ComputeQl(SistemaUso.Entrepiso, cargas);
        var qu = CalculoEngine.ComputeQu(qd, ql, cargas.Factores);

        Assert.Equal(qmamp, r.Qmamp, 6);
        Assert.Equal(qmap, r.Qmap, 6);
        Assert.Equal(qd, r.Qd, 6);
        Assert.Equal(ql, r.Ql, 6);
        Assert.Equal(qu, r.Qu, 6);
        // Ancla independiente: 1.2·(0.541+0.189) + 1.6·0.20 = 1.196 t/m²
        Assert.Equal(1.196, r.Qu, 3);
    }
}

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

    [Fact]
    public void AplicarCargaUltima_escribe_Wu_en_cada_losa_con_mamposteria_compartida()
    {
        var cargas = CargasGlobales.SemillaPorDefecto();
        var sistema = new Sistema { Uso = SistemaUso.Entrepiso };
        var l1 = new Losa { Lx = 4, Ly = 5, Espesor = 0.12 };  // área 20
        var l2 = new Losa { Lx = 5, Ly = 4, Espesor = 0.15 };  // área 20
        sistema.Losas.Add(l1);
        sistema.Losas.Add(l2);
        sistema.Muros.Add(Mur(5.0, 0.15, 2.8));                // Qmamp = 3.78 ton

        var res = CargaUltimaCalculator.AplicarCargaUltima(sistema, cargas);

        // Mampostería repartida sobre el área TOTAL (40 m²) → qmap uniforme;
        // cada losa difiere sólo por su espesor en el qd.
        var qmamp = 1.8 * 5.0 * 0.15 * 2.8;
        var qmap = CalculoEngine.ComputeQmap(qmamp, 40.0);
        var ql = CalculoEngine.ComputeQl(SistemaUso.Entrepiso, cargas);
        var qu1 = CalculoEngine.ComputeQu(
            CalculoEngine.ComputeQd(0.12, cargas, SistemaUso.Entrepiso, qmap), ql, cargas.Factores);
        var qu2 = CalculoEngine.ComputeQu(
            CalculoEngine.ComputeQd(0.15, cargas, SistemaUso.Entrepiso, qmap), ql, cargas.Factores);

        Assert.Equal(2, res.Count);
        Assert.Equal(qu1, l1.Carga, 6);     // Wu escrito en la losa
        Assert.Equal(qu2, l2.Carga, 6);
        Assert.Equal(qmap, res[0].Qmap, 6); // qmap compartido entre losas
        Assert.Equal(qmap, res[1].Qmap, 6);
    }

    [Fact]
    public void AplicarCargaUltima_edificio_recorre_todos_los_niveles_y_sistemas()
    {
        var cargas = CargasGlobales.SemillaPorDefecto();
        var edificio = new Edificio();
        var losas = new List<Losa>();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var nivel = new Nivel { Cota = cota, Nombre = $"N{cota}" };
            var sistema = new Sistema { Uso = SistemaUso.Entrepiso };
            var losa = new Losa { Lx = 4, Ly = 5, Espesor = 0.12, Carga = 0 };
            sistema.Losas.Add(losa);
            losas.Add(losa);
            nivel.Sistemas.Add(sistema);
            edificio.Niveles.Add(nivel);
        }

        var res = CargaUltimaCalculator.AplicarCargaUltima(edificio, cargas);

        // Una entrada por losa del edificio, y todas con Wu escrito (> 0).
        Assert.Equal(2, res.Count);
        Assert.All(losas, l => Assert.True(l.Carga > 0));
        Assert.All(res, r => Assert.True(r.Qu > 0));
    }
}

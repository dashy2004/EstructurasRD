using System.Linq;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del cableado UI del item D: <see cref="MainViewModel.AplicarCargaUltimaConDesglose"/>
/// debe aplicar Wu a cada losa y devolver una fila por losa, alineada (la
/// <c>Qu</c> de la fila es exactamente el Wu escrito en esa losa). El cálculo
/// puro vive en <c>CargaUltimaCalculatorTests</c>; aquí se fija solo el mapeo
/// del VM (orden, índice 1-based, espesor y alineación fila↔losa).
/// </summary>
public class CargaUltimaDesgloseVmTests
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
    public void AplicarCargaUltimaConDesglose_una_fila_por_losa_alineada_y_escribe_Wu()
    {
        var vm = new MainViewModel();
        var sistema = vm.SistemaActivo;
        sistema.Uso = SistemaUso.Entrepiso;
        sistema.Muros.Add(Mur(5.0, 0.15, 2.8));   // Qmamp > 0

        var filas = vm.AplicarCargaUltimaConDesglose();

        var delSistema = filas.Where(f => f.Sistema == sistema.Nombre).ToList();
        Assert.NotEmpty(delSistema);
        Assert.Equal(sistema.Losas.Count, delSistema.Count);

        for (int i = 0; i < sistema.Losas.Count; i++)
        {
            Assert.Equal(i + 1, delSistema[i].Losa);                          // índice 1-based
            Assert.Equal(sistema.Losas[i].Espesor, delSistema[i].Espesor, 6); // espesor de ESA losa
            Assert.Equal(sistema.Losas[i].Carga, delSistema[i].Qu, 6);        // alineación: fila.Qu == Wu escrito
            Assert.True(sistema.Losas[i].Carga > 0);                          // Wu efectivamente aplicado
            Assert.True(delSistema[i].Qmamp > 0);                             // peso de muros propagado
        }
    }
}

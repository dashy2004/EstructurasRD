using System.Linq;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// F3 GATE A: una losa con tipo sin mapear NO aborta el cálculo del sistema —
/// se omite, se registra en LosasNoParseadas, y las demás losas salen completas
/// (mismo patrón por-losa que MotorFeaService.CalcularSistemaConMotorAsync).
/// El tipo 99 está fuera del catálogo y nunca tendrá mapeo: el test sigue
/// siendo significativo después de completar el mapeo de los 23 códigos.
/// </summary>
public class CapturaPorLosaTests
{
    private static Sistema SistemaConLosaSinMapeo()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 40, Carga = 0.720, Espesor = 0.200, Lx = 6.85, Ly = 6.65, Rec = 0.025 });
        s.Losas.Add(new Losa { Id = 2, Tipo = 99, Carga = 0.720, Espesor = 0.200, Lx = 6.85, Ly = 6.65, Rec = 0.025 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" }); // referencia la losa omitida
        return s;
    }

    [Fact]
    public void Una_losa_sin_mapeo_no_aborta_el_sistema()
    {
        var salida = SistemaPieperMartensCalculator.Crear().Calcular(SistemaConLosaSinMapeo());

        Assert.Single(salida.Momentos);                    // solo la losa 1
        Assert.Equal(1, salida.Momentos[0].LosaId);
        Assert.Equal(1.280, salida.Momentos[0].Mfx, 0.01); // momentos intactos (RESTAURANTE 2, L1)
        Assert.Contains(2, salida.LosasNoParseadas);       // la omitida queda registrada
        Assert.Single(salida.ArmadurasXCentro);            // sin armaduras de la losa 2
        Assert.Empty(salida.ArmadurasXApoyos);             // el borde 1-2 se omite sin lanzar
    }

    [Fact]
    public void CalcularYAplicar_aplica_las_losas_buenas_y_no_lanza()
    {
        var s = SistemaConLosaSinMapeo();
        SistemaPieperMartensCalculator.Crear().CalcularYAplicar(s);
        Assert.True(s.Losas.First(l => l.Id == 1).Mfx > 0);
        Assert.Null(s.Losas.First(l => l.Id == 2).Mfx); // la omitida no se toca (Mfx es double?, default null)
    }
}

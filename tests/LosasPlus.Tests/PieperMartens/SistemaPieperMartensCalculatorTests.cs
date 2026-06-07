using System.Linq;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// Fase 3a (orquestador) del motor nativo Pieper-Martens. Reconstruye el sistema
/// RESTAURANTE 2 en código y verifica que el <see cref="SalidaPerdomo"/> producido
/// reproduce <c>Losas.exe</c> en momentos, balanceo de apoyos y As de vano.
/// (El detallado de espaciamiento y el caso one-way de la losa 5 se validan aparte.)
/// </summary>
public class SistemaPieperMartensCalculatorTests
{
    private static Sistema Restaurante2()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        void L(int id, int tipo, double w, double h, double lx, double ly)
            => s.Losas.Add(new Losa { Id = id, Tipo = tipo, Carga = w, Espesor = h, Lx = lx, Ly = ly, Rec = 0.025 });

        L(1, 40, 0.720, 0.200, 6.850, 6.650);
        L(2, 40, 0.720, 0.200, 6.850, 6.650);
        L(3, 40, 0.720, 0.200, 6.850, 6.450);
        L(4, 40, 0.720, 0.200, 6.850, 6.450);
        L(5, 71, 0.720, 0.120, 13.650, 1.200);

        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        s.BordesX.Add(new BordeAdic { BI = 2, BJ = 4, Balanceo = "S" });
        s.BordesY.Add(new BordeAdic { BI = 1, BJ = 3, Balanceo = "S" });
        s.BordesY.Add(new BordeAdic { BI = 2, BJ = 4, Balanceo = "S" });
        s.BordesY.Add(new BordeAdic { BI = 5, BJ = 2, Balanceo = "N" });
        s.BordesY.Add(new BordeAdic { BI = 5, BJ = 1, Balanceo = "N" });
        return s;
    }

    private static readonly SalidaPerdomo Salida =
        SistemaPieperMartensCalculator.Crear().Calcular(Restaurante2());

    [Fact]
    public void Momentos_de_las_5_losas_coinciden()
    {
        Assert.Equal(5, Salida.Momentos.Count);
        var m1 = Salida.Momentos.Single(m => m.LosaId == 1);
        Assert.Equal(1.280, m1.Mfx, 0.01);
        Assert.Equal(1.358, m1.Mfy, 0.01);
        Assert.Equal(1.987, m1.NMSx, 0.01);
        Assert.Equal(2.108, m1.NMSy, 0.01);

        var m5 = Salida.Momentos.Single(m => m.LosaId == 5);
        Assert.Equal(0.000, m5.Mfx, 0.01);
        Assert.Equal(0.518, m5.NMSy, 0.01);
    }

    [Theory]
    [InlineData(2, 4, 1.923)] // X 2-4 (balanceado)
    public void Apoyos_X_balanceados_coinciden(int i, int j, double muIJ)
    {
        var a = Salida.ArmadurasXApoyos.Single(x => x.BordeI == i && x.BordeJ == j);
        Assert.Equal(muIJ, a.MuIJ, 0.01);
    }

    [Theory]
    [InlineData(1, 3, 2.102)] // Y 1-3 (balanceado)
    [InlineData(5, 2, 0.518)] // Y 5-2 (vuelo, no balancea)
    public void Apoyos_Y_balanceados_coinciden(int i, int j, double muIJ)
    {
        var a = Salida.ArmadurasYApoyos.Single(x => x.BordeI == i && x.BordeJ == j);
        Assert.Equal(muIJ, a.MuIJ, 0.01);
    }

    [Fact]
    public void Vano_As_minimo_gobierna_en_losas_two_way()
    {
        // Losas 1-4: As por temperatura = 0.0018·100·20 = 3.60 cm²/m gobierna.
        foreach (int id in new[] { 1, 2, 3, 4 })
        {
            Assert.Equal(3.60, Salida.ArmadurasXCentro.Single(a => a.LosaId == id).As, 0.01);
            Assert.Equal(3.60, Salida.ArmadurasYCentro.Single(a => a.LosaId == id).As, 0.01);
        }
    }

    [Fact]
    public void CalcularYAplicar_pobla_SalidaPerdomo_y_momentos_de_losa()
    {
        // Esto es lo que ejecuta el comando "Calcular nativo" de la pestaña Engine:
        // calcula y deja el resultado visible en el modelo (Aceros, Memoria).
        var s = Restaurante2();
        var salida = SistemaPieperMartensCalculator.Crear().CalcularYAplicar(s);

        Assert.True(s.TieneSalidaPerdomo);
        Assert.Same(salida, s.SalidaPerdomo);

        var l1 = s.Losas.Single(x => x.Id == 1);
        Assert.Equal(1.280, l1.Mfx!.Value, 0.01);
        Assert.Equal(1.358, l1.Mfy!.Value, 0.01);
        Assert.Equal(1.987, l1.MSx!.Value, 0.01);
        Assert.Equal(2.108, l1.MSy!.Value, 0.01);
    }

    [Fact]
    public void Losa5_one_way_solo_arma_la_direccion_soportada()
    {
        // Tipo 71: franja que vuela sobre Ly. El .TXT da acero de distribución
        // (mínimo, H=12 → 2.16) según X y CERO acero de vano según Y.
        var x5 = Salida.ArmadurasXCentro.Single(a => a.LosaId == 5);
        var y5 = Salida.ArmadurasYCentro.Single(a => a.LosaId == 5);

        Assert.Equal(2.16, x5.As, 0.01);
        Assert.Equal(0.00, y5.As, 0.01);
    }
}

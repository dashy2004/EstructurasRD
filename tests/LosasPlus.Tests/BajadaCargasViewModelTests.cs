using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del ViewModel «Bajada de cargas» (Fase J.4): carga las filas del
/// edificio activo, expone la carga en base y recalcula la zapata al cambiar la
/// presión admisible.
/// </summary>
public class BajadaCargasViewModelTests
{
    private static Edificio EdificioDosNiveles()
    {
        var ed = new Edificio();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var n = new Nivel { Cota = cota };
            var s = new Sistema();
            s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // 150 por nivel
            n.Sistemas.Add(s);
            ed.Niveles.Add(n);
        }
        return ed;
    }

    [Fact]
    public void Carga_filas_y_carga_en_base_del_edificio()
    {
        var vm = new BajadaCargasViewModel(() => EdificioDosNiveles());
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal(300, vm.CargaEnBase, 6);
    }

    [Fact]
    public void Presion_admisible_recalcula_la_zapata()
    {
        var vm = new BajadaCargasViewModel(() => EdificioDosNiveles())
        {
            PresionAdmisible = 15 // 300 / 15 = 20 m²
        };
        Assert.Equal(20, vm.AreaZapata, 6);

        vm.PresionAdmisible = 30; // 300 / 30 = 10 m²
        Assert.Equal(10, vm.AreaZapata, 6);
    }

    [Fact]
    public void Recalcular_refleja_cambios_del_edificio()
    {
        var ed = EdificioDosNiveles();
        var vm = new BajadaCargasViewModel(() => ed);
        Assert.Equal(2, vm.Filas.Count);

        var n = new Nivel { Cota = 6 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 });
        n.Sistemas.Add(s);
        ed.Niveles.Add(n);

        vm.Recalcular();
        Assert.Equal(3, vm.Filas.Count);
        Assert.Equal(450, vm.CargaEnBase, 6);
    }
}

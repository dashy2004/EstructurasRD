using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del <see cref="AcerosViewModel"/>: sólo las losas con momentos
/// importados generan filas (4 por losa), y el recálculo en vivo responde a
/// los parámetros editables.
/// </summary>
public class AcerosViewModelTests
{
    private static Sistema SistemaConUnaLosaConMomentos()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.SalidaPerdomo = new SalidaPerdomo();

        // Losa CON momentos (importada del .TXT).
        s.Losas.Add(new Losa
        {
            Id = 1, Tipo = 33, Lx = 4.0, Ly = 5.0, Espesor = 0.20, Carga = 0.8,
            Mfx = 1.0, Mfy = 0.8, MSx = 1.5, MSy = 1.2,
        });
        // Losa SIN momentos (aún no calculada) — debe quedar excluida.
        s.Losas.Add(new Losa { Id = 2, Tipo = 21, Lx = 3.0, Ly = 3.0, Espesor = 0.15 });
        return s;
    }

    [Fact]
    public void Solo_losas_con_momentos_generan_filas_cuatro_por_losa()
    {
        var s = SistemaConUnaLosaConMomentos();
        var vm = new AcerosViewModel(() => s);

        Assert.True(vm.HayMomentos);
        Assert.Equal(4, vm.TotalFranjas);                 // 1 losa × 4 franjas
        Assert.All(vm.Filas, f => Assert.Equal(1, f.LosaId));
        Assert.Contains(vm.Filas, f => f.Franja == "X centro");
        Assert.Contains(vm.Filas, f => f.Franja == "Y apoyo");
    }

    [Fact]
    public void Sin_momentos_no_hay_filas_y_muestra_mensaje_guia()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 21, Lx = 3, Ly = 3, Espesor = 0.15 });
        var vm = new AcerosViewModel(() => s);

        Assert.False(vm.HayMomentos);
        Assert.Equal(0, vm.TotalFranjas);
        Assert.Contains(".TXT", vm.MensajeVacio);
    }

    [Fact]
    public void Cambiar_recubrimiento_recalcula_en_vivo()
    {
        var s = SistemaConUnaLosaConMomentos();
        var vm = new AcerosViewModel(() => s);
        double dAntes = vm.Filas[0].DCm;

        vm.RecubrimientoCm = 4.0;   // mayor recubrimiento → menor canto útil

        Assert.Equal(4, vm.TotalFranjas);                 // sigue habiendo 4 filas
        Assert.True(vm.Filas[0].DCm < dAntes);            // d disminuyó
    }

    [Fact]
    public void FranjasNoCumplen_cuenta_las_marcadas()
    {
        var s = SistemaConUnaLosaConMomentos();
        var vm = new AcerosViewModel(() => s);
        // Con momentos moderados y h=20cm todas deberían cumplir.
    }

    [Fact]
    public void Agrupa_franjas_por_losa_en_propiedad_Grupos()
    {
        var s = SistemaConUnaLosaConMomentos();
        var vm = new AcerosViewModel(() => s);

        Assert.Single(vm.Grupos);
        var grupo = vm.Grupos[0];
        Assert.Equal(1, grupo.LosaId);
        Assert.Equal(4, grupo.Franjas.Count);
        Assert.Equal(4.0, grupo.Lx);
        Assert.Equal(5.0, grupo.Ly);
        Assert.Equal(0.20, grupo.Espesor);
    }

    [Fact]
    public void Aplicar_override_escribe_en_SalidaPerdomo()
    {
        var s = SistemaConUnaLosaConMomentos();
        var vm = new AcerosViewModel(() => s);

        var filaXCentro = vm.Filas.First(f => f.Franja == "X centro");
        
        // Al aplicar override, debería actualizarse la colección ArmadurasXCentro del Sistema Activo
        filaXCentro.AplicarOverride(6, 10.0); // Barra #6 a 10cm

        Assert.Single(s.SalidaPerdomo.ArmadurasXCentro);
        var armadura = s.SalidaPerdomo.ArmadurasXCentro[0];
        Assert.Equal(1, armadura.LosaId);
        Assert.Equal(filaXCentro.Disponer, armadura.Disponer);
        Assert.Equal(filaXCentro.AsProvistaCm2M, armadura.AsReal);
    }
}

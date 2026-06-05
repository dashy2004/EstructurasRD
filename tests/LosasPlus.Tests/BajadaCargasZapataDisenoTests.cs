using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del item H (mejora): el peralte h y los materiales f'c/fy de la zapata
/// son editables y gobiernan el diseño estructural; al editarlos, la tabla de
/// diseño se recalcula en vivo y el peralte elegido se persiste en la zapata.
/// </summary>
public class BajadaCargasZapataDisenoTests
{
    private static Edificio EdificioConColumnas(double cargaLosa)
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = cargaLosa });
        nivel.Sistemas.Add(s);
        for (int i = 1; i <= 3; i++)
            nivel.Columnas.Add(new Columna { Nombre = $"C-{i}", Base = 0.30, Peralte = 0.30 });
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Peralte_gobierna_el_diseno_se_refleja_en_vivo_y_se_persiste()
    {
        var ed = EdificioConColumnas(cargaLosa: 4.0);   // base 400 t → ~133 t/columna
        var vm = new BajadaCargasViewModel(() => ed) { PresionAdmisible = 15 };

        vm.PredimensionarZapatas();
        Assert.NotEmpty(vm.ZapatasDiseno);

        // h por defecto = 0.40 m, persistido en la zapata.
        double ratioGrueso = vm.ZapatasDiseno[0].PunzRatio;
        Assert.Equal(0.40, ed.Niveles[0].Columnas[0].Zapata!.Peralte, 6);

        // Bajar h re-diseña EN VIVO (sin volver a tocar el botón) y empeora el
        // punzonamiento (menor canto útil → mayor ratio).
        vm.PeralteZapataM = 0.20;
        double ratioDelgado = vm.ZapatasDiseno[0].PunzRatio;
        Assert.True(ratioDelgado > ratioGrueso,
            $"esperado ratio mayor con h=0.20 ({ratioDelgado}) que con h=0.40 ({ratioGrueso})");
        Assert.Equal(0.20, ed.Niveles[0].Columnas[0].Zapata!.Peralte, 6);
    }

    [Fact]
    public void Materiales_fc_fy_son_editables_y_recalculan_en_vivo()
    {
        var ed = EdificioConColumnas(cargaLosa: 4.0);
        var vm = new BajadaCargasViewModel(() => ed) { PresionAdmisible = 15 };
        vm.PredimensionarZapatas();

        double qu = vm.ZapatasDiseno[0].Diseno.QuMPa;   // presión de contacto (no depende de f'c)
        double punzGrueso = vm.ZapatasDiseno[0].Diseno.Punzonamiento.PhiVcN;

        // Subir f'c aumenta la capacidad a punzonamiento (φVc ∝ √f'c), en vivo.
        vm.FcMPa = 35.0;
        double punzMejor = vm.ZapatasDiseno[0].Diseno.Punzonamiento.PhiVcN;
        Assert.True(punzMejor > punzGrueso);
        // La presión de contacto qu = Pu/(B·L) no cambia con f'c.
        Assert.Equal(qu, vm.ZapatasDiseno[0].Diseno.QuMPa, 6);
    }
}

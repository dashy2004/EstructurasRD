using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del editor de columnas (Fase J.10): agregar/eliminar columnas del
/// primer nivel del edificio activo.
/// </summary>
public class ColumnasEditorViewModelTests
{
    private static Edificio UnNivel()
    {
        var ed = new Edificio();
        ed.Niveles.Add(new Nivel { Cota = 0 });
        return ed;
    }

    [Fact]
    public void Agregar_inserta_columna_correlativa_y_la_selecciona()
    {
        var ed = UnNivel();
        var vm = new ColumnasEditorViewModel(() => ed);

        var c1 = vm.Agregar();
        var c2 = vm.Agregar();

        Assert.Equal(2, ed.Niveles[0].Columnas.Count);
        Assert.Equal("C-1", c1!.Nombre);
        Assert.Equal("C-2", c2!.Nombre);
        Assert.Same(c2, vm.Seleccionada);
        Assert.Same(ed.Niveles[0].Columnas, vm.Columnas);
    }

    [Fact]
    public void Eliminar_quita_la_columna_seleccionada()
    {
        var ed = UnNivel();
        var vm = new ColumnasEditorViewModel(() => ed);
        vm.Agregar();
        var c2 = vm.Agregar();

        vm.Seleccionada = c2;
        vm.Eliminar();

        Assert.Single(ed.Niveles[0].Columnas);
        Assert.Null(vm.Seleccionada);
    }

    [Fact]
    public void Sin_edificio_no_hace_nada()
    {
        var vm = new ColumnasEditorViewModel(() => null);
        Assert.Null(vm.Columnas);
        Assert.Null(vm.Agregar());
        vm.Eliminar(); // no lanza
    }

    [Fact]
    public void Edita_las_columnas_del_nivel_seleccionado()
    {
        var ed = new Edificio();
        var n0 = new Nivel { Nombre = "Planta Baja", Cota = 0 };
        var n1 = new Nivel { Nombre = "Nivel 1", Cota = 3 };
        ed.Niveles.Add(n0);
        ed.Niveles.Add(n1);

        var vm = new ColumnasEditorViewModel(() => ed);

        // Por defecto se selecciona el primer nivel.
        Assert.Equal(2, vm.Niveles.Count);
        Assert.Same(n0, vm.NivelSeleccionado);
        vm.Agregar();
        Assert.Single(n0.Columnas);
        Assert.Empty(n1.Columnas);

        // Al cambiar de nivel, las columnas reflejan el nuevo y se agrega allí.
        vm.NivelSeleccionado = n1;
        Assert.Same(n1.Columnas, vm.Columnas);
        vm.Agregar();
        Assert.Single(n0.Columnas);
        Assert.Single(n1.Columnas);
    }

    [Fact]
    public void TomarPuDelDescenso_setea_PuKN_desde_el_descenso_equitativo()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // 1.5·100 = 150 ton en base
        nivel.Sistemas.Add(s);
        nivel.Columnas.Add(new Columna { Nombre = "C1" });
        nivel.Columnas.Add(new Columna { Nombre = "C2" });
        ed.Niveles.Add(nivel);
        var vm = new ColumnasEditorViewModel(() => ed);

        vm.TomarPuDelDescenso();

        // CargaEnBase=150 ton / 2 columnas = 75 ton → 75 × 9.80665 = 735.49875 kN.
        Assert.Equal(735.49875, vm.PuKN, 4);
    }

    [Fact]
    public void EsbeltezActual_se_calcula_para_la_columna_seleccionada()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var col = new Columna { Nombre = "C1", Base = 0.4, Peralte = 0.6 }; // 400×600 mm
        nivel.Columnas.Add(col);
        ed.Niveles.Add(nivel);
        var vm = new ColumnasEditorViewModel(() => ed);

        vm.Seleccionada = col;
        vm.LuMm = 4000;

        var e = vm.EsbeltezActual;
        Assert.NotNull(e);
        Assert.Equal(0.3 * 600, e!.RMm, 6);             // r = 0.3·h
        Assert.Equal(4000.0 / 180.0, e.KLuSobreR, 6);   // k·Lu/r = 1·4000/180
    }
}

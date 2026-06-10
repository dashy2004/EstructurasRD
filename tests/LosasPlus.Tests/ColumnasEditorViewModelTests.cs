using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;
using LosasPlus.ViewModels;
using LosasPlus.Vigas;
using OxyPlot.Series;
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
        var vm = new ColumnasEditorViewModel(() => ed, () => ed.Niveles[0]);

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
        var vm = new ColumnasEditorViewModel(() => ed, () => ed.Niveles[0]);
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
        var vm = new ColumnasEditorViewModel(() => null, () => null);
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

        Nivel? nivelActivo = n0;
        var vm = new ColumnasEditorViewModel(() => ed, () => nivelActivo);

        vm.Agregar();
        Assert.Single(n0.Columnas);
        Assert.Empty(n1.Columnas);

        // Al cambiar de nivel, las columnas reflejan el nuevo y se agrega allí.
        nivelActivo = n1;
        vm.Recargar();
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
        var vm = new ColumnasEditorViewModel(() => ed, () => nivel);

        vm.TomarPuDelDescenso();

        // CargaEnBase=150 ton / 2 columnas = 75 ton → 75 × 9.80665 = 735.49875 kN.
        Assert.Equal(735.49875, vm.PuKN, 4);
    }

    [Fact]
    public void TomarPuDelDescenso_usa_geometrico_para_la_columna_seleccionada()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        Viga V(double ox, double oy, double len, double ang)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = len });
            return v;
        }
        nivel.Vigas.Add(V(0, 4, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 90));
        var c04 = new Columna { Nombre = "C04", CoordenadaX = 0, CoordenadaY = 4 };
        nivel.Columnas.Add(new Columna { Nombre = "C00", CoordenadaX = 0, CoordenadaY = 0 });
        nivel.Columnas.Add(c04);
        ed.Niveles.Add(nivel);
        var vm = new ColumnasEditorViewModel(() => ed, () => nivel) { Seleccionada = c04 };

        vm.TomarPuDelDescenso();

        // Wu tributaria de C04 = 60 t → 60 × 9.80665 kN, NO CargaEnBase/2 equitativo.
        Assert.Equal(60 * DescensoColumnas.KN_por_Ton, vm.PuKN, 3);
    }

    // ---- D2: editor autónomo (Niveles/NivelSeleccionado reales + recalc al editar) ----

    [Fact]
    public void Niveles_expone_los_niveles_del_edificio_y_NivelSeleccionado_arranca_en_el_activo()
    {
        var ed = new Edificio();
        var n0 = new Nivel { Nombre = "PB", Cota = 0 };
        var n1 = new Nivel { Nombre = "N1", Cota = 3 };
        ed.Niveles.Add(n0);
        ed.Niveles.Add(n1);

        var vm = new ColumnasEditorViewModel(() => ed, () => n0);

        Assert.NotNull(vm.Niveles);
        Assert.Equal(2, vm.Niveles!.Count);            // los niveles reales del edificio
        Assert.Same(n0, vm.NivelSeleccionado);          // arranca en el nivel activo
    }

    [Fact]
    public void Seleccionar_un_nivel_con_columnas_llena_Columnas()
    {
        var ed = new Edificio();
        var n0 = new Nivel { Nombre = "PB", Cota = 0 };   // vacío
        var n1 = new Nivel { Nombre = "N1", Cota = 3 };
        n1.Columnas.Add(new Columna { Id = 1, Nombre = "C-1", Base = 0.4, Peralte = 0.4 });
        ed.Niveles.Add(n0);
        ed.Niveles.Add(n1);

        var vm = new ColumnasEditorViewModel(() => ed, () => n0);
        Assert.Empty(vm.Columnas!);                       // PB no tiene columnas

        // El usuario elige N1 en el ComboBox del editor → Columnas se llena.
        vm.NivelSeleccionado = n1;
        Assert.Same(n1.Columnas, vm.Columnas);
        Assert.NotEmpty(vm.Columnas!);
        Assert.Null(vm.Seleccionada);                     // la selección se limpia al cambiar de nivel
    }

    [Fact]
    public void Sync_con_NivelActivo_via_Recargar_mueve_el_NivelSeleccionado()
    {
        var ed = new Edificio();
        var n0 = new Nivel { Nombre = "PB", Cota = 0 };
        var n1 = new Nivel { Nombre = "N1", Cota = 3 };
        n1.Columnas.Add(new Columna { Id = 1, Nombre = "C-1" });
        ed.Niveles.Add(n0);
        ed.Niveles.Add(n1);

        Nivel nivelActivo = n0;
        var vm = new ColumnasEditorViewModel(() => ed, () => nivelActivo);
        Assert.Same(n0, vm.NivelSeleccionado);

        // MainViewModel.NivelActivo cambió → invoca Recargar() (D2 sync).
        nivelActivo = n1;
        vm.Recargar();
        Assert.Same(n1, vm.NivelSeleccionado);
        Assert.Same(n1.Columnas, vm.Columnas);
    }

    [Fact]
    public void Seleccionar_columna_produce_seccion_y_diagrama_PM()
    {
        var ed = new Edificio();
        var niv = new Nivel { Cota = 0 };
        var col = new Columna { Id = 1, Nombre = "C-1", Base = 0.40, Peralte = 0.40, Altura = 3.0 };
        niv.Columnas.Add(col);
        ed.Niveles.Add(niv);
        var vm = new ColumnasEditorViewModel(() => ed, () => niv);

        vm.Seleccionada = col;

        Assert.NotNull(vm.ModeloSeccionColumna);          // sección transversal
        Assert.NotNull(vm.ModeloInteraccion);             // diagrama P-M
        // El P-M tiene la curva con puntos.
        var curva = vm.ModeloInteraccion!.Series.OfType<LineSeries>().First();
        Assert.NotEmpty(curva.Points);
    }

    [Fact]
    public void Editar_Base_de_la_columna_seleccionada_recalcula_el_diagrama_PM()
    {
        var ed = new Edificio();
        var niv = new Nivel { Cota = 0 };
        var col = new Columna { Id = 1, Nombre = "C-1", Base = 0.30, Peralte = 0.40, Altura = 3.0 };
        niv.Columnas.Add(col);
        ed.Niveles.Add(niv);
        var vm = new ColumnasEditorViewModel(() => ed, () => niv);
        vm.PuKN = 800;   // demanda fija para comparar la capacidad del diagrama
        vm.Seleccionada = col;

        double poAntes = vm.DisenoActual!.PoN;
        var plotAntes = vm.ModeloInteraccion;

        // Editar la sección (más base → más concreto → más Po) debe recalcular.
        col.Base = 0.60;

        Assert.True(vm.DisenoActual!.PoN > poAntes);       // RecalcularDiseno corrió
        Assert.NotSame(plotAntes, vm.ModeloInteraccion);   // el modelo P-M se reconstruyó
    }

    // ---- Tests del gate CanExecute (fix frozen-buttons) ----

    [Fact]
    public void EliminarCommand_inhabilitado_sin_seleccion_y_se_habilita_al_seleccionar()
    {
        var ed = UnNivel();
        var vm = new ColumnasEditorViewModel(() => ed, () => ed.Niveles[0]);

        // Sin columna seleccionada el predicado debe devolver false.
        Assert.False(vm.EliminarCommand.CanExecute(null));

        // Suscribir antes de mutar.
        bool eventFired = false;
        vm.EliminarCommand.CanExecuteChanged += (_, _) => eventFired = true;

        // Agregar y seleccionar una columna → true.
        vm.Agregar();   // Agregar() llama Seleccionada = columna internamente
        Assert.True(vm.EliminarCommand.CanExecute(null));
        _ = eventFired; // evento encolado vía Dispatcher.UIThread.Post en tests sin UI loop
    }

    [Fact]
    public void EliminarCommand_vuelve_a_false_tras_limpiar_la_seleccion()
    {
        var ed = UnNivel();
        var vm = new ColumnasEditorViewModel(() => ed, () => ed.Niveles[0]);
        vm.Agregar();
        Assert.True(vm.EliminarCommand.CanExecute(null));

        bool eventFired = false;
        vm.EliminarCommand.CanExecuteChanged += (_, _) => eventFired = true;

        vm.Seleccionada = null;

        Assert.False(vm.EliminarCommand.CanExecute(null));
        _ = eventFired;
    }

    [Fact]
    public void EsbeltezActual_se_calcula_para_la_columna_seleccionada()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var col = new Columna { Nombre = "C1", Base = 0.4, Peralte = 0.6 }; // 400×600 mm
        nivel.Columnas.Add(col);
        ed.Niveles.Add(nivel);
        var vm = new ColumnasEditorViewModel(() => ed, () => nivel);

        vm.Seleccionada = col;
        vm.LuMm = 4000;

        var e = vm.EsbeltezActual;
        Assert.NotNull(e);
        Assert.Equal(0.3 * 600, e!.RMm, 6);             // r = 0.3·h
        Assert.Equal(4000.0 / 180.0, e.KLuSobreR, 6);   // k·Lu/r = 1·4000/180
    }
}

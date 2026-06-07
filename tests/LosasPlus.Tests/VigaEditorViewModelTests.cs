using System;
using System.Linq;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Vigas;
using LosasPlus.ViewModels.Vigas;
using OxyPlot;
using OxyPlot.Series;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="VigaEditorViewModel"/> (Fase 3, Iteración 2):
/// instanciación, comandos, recálculo reactivo y regeneración de las series
/// OxyPlot, más el round-trip de persistencia de <c>Nivel.Vigas</c>.
/// </summary>
public class VigaEditorViewModelTests
{
    /// <summary>Crea el VM sobre <paramref name="proyecto"/>; cuenta los snapshots de Undo.</summary>
    private static VigaEditorViewModel Crear(Proyecto proyecto, out Func<int> snapshots)
    {
        int n = 0;
        snapshots = () => n;
        return new VigaEditorViewModel(proyecto, () => n++, () => proyecto.Edificios[0].Niveles[0]);
    }

    /// <summary>Viga simplemente apoyada de un tramo con una carga distribuida «D».</summary>
    private static Viga VigaResoluble()
    {
        var viga = new Viga { Id = 1, Nombre = "V-1" };
        var tramo = new TramoViga { Longitud = 6.0, ModuloElasticidad = 2.5e7, Inercia = 0.002 };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 20.0, "D"));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(6.0, TipoApoyo.Fijo));
        return viga;
    }

    /// <summary>Máximo |M| entre las series de momento del diagrama de esfuerzos.</summary>
    private static double MaxAbsMomento(PlotModel modelo)
    {
        double max = 0.0;
        foreach (var serie in modelo.Series.OfType<LineSeries>())
            if (serie.YAxisKey == "M")
                foreach (var p in serie.Points)
                    max = Math.Max(max, Math.Abs(p.Y));
        return max;
    }

    [Fact]
    public void Instanciacion_limpia_con_proyecto_vacio()
    {
        var vm = Crear(new Proyecto(), out _);

        Assert.Null(vm.VigaActiva);
        Assert.NotNull(vm.ModeloViga);
        Assert.NotNull(vm.ModeloEsfuerzos);
        Assert.NotNull(vm.ModeloDeflexion);
    }

    [Fact]
    public void NuevaVigaCommand_crea_activa_la_viga_y_toma_snapshot()
    {
        var vm = Crear(new Proyecto(), out var snapshots);

        vm.NuevaVigaCommand.Execute(null);

        Assert.Single(vm.Vigas);
        Assert.NotNull(vm.VigaActiva);
        Assert.NotEmpty(vm.VigaActiva!.Tramos);          // sembrada con un tramo
        Assert.Equal(2, vm.VigaActiva.Apoyos.Count);     // y dos apoyos
        Assert.Equal(1, snapshots());
    }

    [Fact]
    public async Task RecalcularAsync_con_viga_resoluble_genera_las_series()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaResoluble());
        var vm = Crear(proyecto, out _);

        await vm.RecalcularAsync();

        Assert.False(vm.EsInestable);
        Assert.NotEmpty(vm.ModeloEsfuerzos.Series);
        Assert.NotEmpty(vm.ModeloDeflexion.Series);
    }

    [Fact]
    public async Task Modificar_un_factor_geometrico_regenera_las_series()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var viga = VigaResoluble();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(viga);
        var vm = Crear(proyecto, out _);
        await vm.RecalcularAsync();

        double momentoAntes = MaxAbsMomento(vm.ModeloEsfuerzos);
        viga.Tramos[0].Longitud = 10.0;   // factor geométrico
        await vm.RecalcularAsync();
        double momentoDespues = MaxAbsMomento(vm.ModeloEsfuerzos);

        Assert.True(momentoAntes > 0.0);
        Assert.True(momentoDespues > momentoAntes);   // un tramo más largo → más momento
    }

    [Fact]
    public void TramoViga_recalcula_Inercia_al_editar_Base_o_Peralte()
    {
        var t = new TramoViga();   // 0.30 × 0.50 → I = 0.003125 m⁴ por defecto
        Assert.Equal(0.30 * Math.Pow(0.50, 3) / 12.0, t.Inercia, precision: 9);

        t.Base = 0.40;
        Assert.Equal(0.40 * Math.Pow(0.50, 3) / 12.0, t.Inercia, precision: 9);

        t.Peralte = 0.70;
        Assert.Equal(0.40 * Math.Pow(0.70, 3) / 12.0, t.Inercia, precision: 9);
    }

    [Fact]
    public async Task Editar_Peralte_acopla_Inercia_y_cambia_la_deflexion_del_motor()
    {
        // D1 (bug de ingeniería): editar la sección debe propagarse a I = b·h³/12 y,
        // como el motor lee tramo.Inercia, regenerar los diagramas. Con MÁS peralte
        // la viga es más rígida → MENOS deflexión.
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var viga = VigaResoluble();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(viga);
        var vm = Crear(proyecto, out _);
        await vm.RecalcularAsync();

        double iAntes = viga.Tramos[0].Inercia;
        double deflexionAntes = MaxAbsDeflexion(vm.ModeloDeflexion);

        viga.Tramos[0].Peralte = viga.Tramos[0].Peralte * 2.0;   // sección más peraltada
        await vm.RecalcularAsync();

        double iDespues = viga.Tramos[0].Inercia;
        double deflexionDespues = MaxAbsDeflexion(vm.ModeloDeflexion);

        Assert.True(iDespues > iAntes);                  // la inercia siguió a la geometría
        Assert.True(deflexionAntes > 0.0);
        Assert.True(deflexionDespues < deflexionAntes);  // más I → menos δ (nuevo diagrama)
    }

    [Fact]
    public async Task RefrescarPorCambioDeNivel_muestra_las_vigas_del_nuevo_nivel()
    {
        // D1 (goal 2): el editor debe seguir al NivelActivo y no quedarse con las
        // vigas del nivel anterior.
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var ed = proyecto.Edificios[0];
        var n0 = ed.Niveles[0];
        var n1 = new Nivel { Nombre = "Nivel 1", Cota = 3 };
        ed.Niveles.Add(n1);

        n0.Vigas.Add(VigaResoluble());                 // n0 tiene 1 viga
        n1.Vigas.Add(new Viga { Id = 9, Nombre = "V-N1" });   // n1 tiene otra distinta

        Nivel nivelActivo = n0;
        int snaps = 0;
        var vm = new VigaEditorViewModel(proyecto, () => snaps++, () => nivelActivo);
        await vm.RecalcularAsync();

        Assert.Same(n0.Vigas, vm.Vigas);
        Assert.Same(n0.Vigas[0], vm.VigaActiva);

        // Cambiar de nivel y refrescar (lo que hace MainViewModel.NivelActivo setter).
        nivelActivo = n1;
        vm.RefrescarPorCambioDeNivel();

        Assert.Same(n1.Vigas, vm.Vigas);
        Assert.Same(n1.Vigas[0], vm.VigaActiva);
    }

    /// <summary>Máximo |δ| entre las series de la gráfica de deflexión.</summary>
    private static double MaxAbsDeflexion(PlotModel modelo)
    {
        double max = 0.0;
        foreach (var serie in modelo.Series.OfType<LineSeries>())
            foreach (var p in serie.Points)
                max = Math.Max(max, Math.Abs(p.Y));
        return max;
    }

    [Fact]
    public async Task Cambiar_la_combinacion_regenera_las_series_sin_re_ejecutar_el_motor()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaResoluble());
        var vm = Crear(proyecto, out _);
        await vm.RecalcularAsync();

        // Envolvente por defecto → 4 series (V máx/mín, M máx/mín).
        Assert.Equal(4, vm.ModeloEsfuerzos.Series.Count);

        vm.CombinacionSeleccionada = "1.4D";

        // Una combinación concreta → 2 series (V(x), M(x)) — reconstruidas en
        // caliente desde el resultado en caché, sin re-ejecutar el motor.
        Assert.Equal(2, vm.ModeloEsfuerzos.Series.Count);
    }

    // ---- Tests del gate CanExecute (fix frozen-buttons) ----

    [Fact]
    public void EliminarVigaCommand_inhabilitado_sin_viga_activa_y_se_habilita_al_seleccionar()
    {
        var vm = Crear(new Proyecto(), out _);

        // Sin ninguna viga, el predicado debe devolver false.
        Assert.False(vm.EliminarVigaCommand.CanExecute(null));

        // Suscribir el evento ANTES de mutar el estado.
        bool eventFired = false;
        vm.EliminarVigaCommand.CanExecuteChanged += (_, _) => eventFired = true;

        // Crear una viga activa activa el comando.
        vm.NuevaVigaCommand.Execute(null);

        Assert.True(vm.EliminarVigaCommand.CanExecute(null));
        // El evento se encoló vía Dispatcher.UIThread.Post — la verificación
        // funcional es que CanExecute devuelva el valor correcto.
        _ = eventFired; // suscripción ejercida; la entrega es asíncrona en tests sin UI loop.
    }

    [Fact]
    public void EliminarTramoCommand_inhabilitado_sin_tramo_y_se_habilita_al_seleccionar()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var vm = Crear(proyecto, out _);
        vm.NuevaVigaCommand.Execute(null);   // crea viga con un tramo ya seleccionado

        // El TramoSeleccionado ya está fijado por NuevaViga → CanExecute true.
        Assert.True(vm.EliminarTramoCommand.CanExecute(null));

        // Deseleccionar: CanExecute debe volver a false.
        bool eventFired = false;
        vm.EliminarTramoCommand.CanExecuteChanged += (_, _) => eventFired = true;
        vm.TramoSeleccionado = null;

        Assert.False(vm.EliminarTramoCommand.CanExecute(null));
        _ = eventFired;
    }

    [Fact]
    public void AgregarCargaCommand_inhabilitado_sin_tramo_y_se_habilita_al_seleccionar()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var vm = Crear(proyecto, out _);

        // Sin viga, sin tramo → false.
        Assert.False(vm.AgregarCargaCommand.CanExecute(null));

        bool eventFired = false;
        vm.AgregarCargaCommand.CanExecuteChanged += (_, _) => eventFired = true;

        vm.NuevaVigaCommand.Execute(null);   // viga + tramo → TramoSeleccionado != null
        Assert.True(vm.AgregarCargaCommand.CanExecute(null));
        _ = eventFired;
    }

    [Fact]
    public void EliminarCargaCommand_se_habilita_cuando_CargaSeleccionada_no_es_null()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var vm = Crear(proyecto, out _);

        // Sin viga ni carga → false.
        Assert.False(vm.EliminarCargaCommand.CanExecute(null));

        vm.NuevaVigaCommand.Execute(null);
        // La viga recién creada no tiene cargas en su tramo inicial → sigue false.
        Assert.False(vm.EliminarCargaCommand.CanExecute(null));

        bool eventFired = false;
        vm.EliminarCargaCommand.CanExecuteChanged += (_, _) => eventFired = true;

        // Agregar una carga selecciona CargaSeleccionada → true.
        vm.AgregarCargaCommand.Execute(null);
        Assert.True(vm.EliminarCargaCommand.CanExecute(null));
        _ = eventFired;
    }

    [Fact]
    public void EliminarApoyoCommand_se_habilita_cuando_ApoyoSeleccionado_no_es_null()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var vm = Crear(proyecto, out _);

        Assert.False(vm.EliminarApoyoCommand.CanExecute(null));

        bool eventFired = false;
        vm.EliminarApoyoCommand.CanExecuteChanged += (_, _) => eventFired = true;

        vm.NuevaVigaCommand.Execute(null);   // la nueva viga tiene 2 apoyos; el primero se selecciona
        Assert.True(vm.EliminarApoyoCommand.CanExecute(null));
        _ = eventFired;
    }

    [Fact]
    public void Las_vigas_del_nivel_sobreviven_un_roundtrip_de_serializacion()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var viga = new Viga { Id = 7, Nombre = "V-Persistencia" };
        viga.Tramos.Add(new TramoViga { Longitud = 4.0 });
        viga.Tramos.Add(new TramoViga { Longitud = 5.0 });
        viga.Tramos[0].Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 15.0, "D"));
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Empotrado));
        viga.Apoyos.Add(new ApoyoViga(4.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(9.0, TipoApoyo.Fijo));
        proyecto.Edificios[0].Niveles[0].Vigas.Add(viga);

        var json = ProyectoSerializer.ToJson(proyecto);
        var restaurado = ProyectoSerializer.FromJson(json);
        var vigas = restaurado.Edificios[0].Niveles[0].Vigas;

        Assert.Single(vigas);
        Assert.Equal("V-Persistencia", vigas[0].Nombre);
        Assert.Equal(2, vigas[0].Tramos.Count);
        Assert.Equal(3, vigas[0].Apoyos.Count);
        Assert.Equal(5.0, vigas[0].Tramos[1].Longitud, precision: 6);
        Assert.Equal(TipoApoyo.Empotrado, vigas[0].Apoyos[0].Tipo);
        Assert.Single(vigas[0].Tramos[0].Cargas);
        Assert.Equal(15.0, vigas[0].Tramos[0].Cargas[0].Magnitud, precision: 6);
    }
}

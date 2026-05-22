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
        return new VigaEditorViewModel(proyecto, () => n++);
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

    /// <summary>
    /// Viga simplemente apoyada de un tramo con una sección RC modesta y una
    /// carga «D» pesada — su envolvente de diseño supera la capacidad φMn.
    /// </summary>
    private static Viga VigaSobreesforzada()
    {
        var viga = new Viga { Id = 2, Nombre = "V-RC" };
        var tramo = new TramoViga { Longitud = 6.0, ModuloElasticidad = 2.5e7, Inercia = 0.002 };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 50.0, "D"));
        tramo.Seccion.Base = 0.30;
        tramo.Seccion.Peralte = 0.60;
        tramo.Seccion.Recubrimiento = 0.06;
        tramo.Seccion.Fc = 28.0;
        tramo.Seccion.Fy = 420.0;
        tramo.Refuerzo.AreaAceroBot = 0.0006;
        tramo.Refuerzo.AreaAceroTop = 0.0006;
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(6.0, TipoApoyo.Fijo));
        return viga;
    }

    /// <summary>Viga con un solo apoyo — un mecanismo, no resoluble.</summary>
    private static Viga VigaInestable()
    {
        var viga = new Viga { Id = 3, Nombre = "V-Mecanismo" };
        viga.Tramos.Add(new TramoViga { Longitud = 6.0 });
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
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

    [Fact]
    public async Task RecalcularAsync_con_viga_resoluble_refleja_la_verificacion_dc()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaSobreesforzada());
        var vm = Crear(proyecto, out _);

        await vm.RecalcularAsync();

        Assert.False(vm.EsInestable);
        Assert.NotEmpty(vm.ModeloVerificacion.Series);       // la curva D/C(x)
        Assert.NotEmpty(vm.ModeloVerificacion.Annotations);  // límite 1.0 + bandas
        Assert.True(vm.RatioMaximoVerificacion > 1.0);
        Assert.NotEmpty(vm.PuntosCriticos);
        Assert.False(vm.VerificacionConforme);
    }

    [Fact]
    public async Task RecalcularAsync_con_viga_inestable_deja_la_verificacion_vacia()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaInestable());
        var vm = Crear(proyecto, out _);

        await vm.RecalcularAsync();

        Assert.True(vm.EsInestable);
        Assert.Empty(vm.ModeloVerificacion.Series);
        Assert.Empty(vm.PuntosCriticos);
        Assert.Equal(0.0, vm.RatioMaximoVerificacion);
        Assert.True(vm.VerificacionConforme);
    }
}

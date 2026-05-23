using System.Linq;
using System.Threading.Tasks;
using LosasPlus.Auditoria;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.ViewModels.Auditoria;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del <see cref="AuditoriaViewModel"/> (Fase 7, Iteración 2):
/// instanciación limpia, reactividad del setter de
/// <see cref="AuditoriaViewModel.Informe"/> ante <c>ActualizarAuditoriaAsync</c>,
/// y filtrado on the fly de <c>MensajesFiltrados</c> sin re-ejecutar el
/// motor.
/// </summary>
public class AuditoriaViewModelTests
{
    [Fact]
    public void Instanciacion_limpia_con_proyecto_vacio()
    {
        var vm = new AuditoriaViewModel(new Proyecto());

        Assert.Null(vm.Informe);
        Assert.False(vm.HayInforme);
        Assert.False(vm.EjecutandoAnalisis);
        Assert.Empty(vm.MensajesFiltrados);
        // Defaults neutros antes del primer análisis: conforme + ratios = 0.
        Assert.True(vm.ProyectoConforme);
        Assert.Equal(0, vm.CantidadAdvertencias);
        Assert.Equal(0, vm.CantidadErrores);
        Assert.Equal(0.0, vm.RatioMaximoGlobal, precision: 6);
        Assert.False(vm.RatioVigasExcede);
        Assert.False(vm.RatioColumnasExcede);
        Assert.False(vm.RatioZapatasExcede);
    }

    [Fact]
    public async Task ActualizarAuditoriaAsync_refleja_el_informe_del_motor()
    {
        // Proyecto seedeado con combinaciones por defecto — limpio, sin
        // elementos: el motor devuelve un informe sin mensajes y conforme.
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var vm = new AuditoriaViewModel(proyecto);

        await vm.ActualizarAuditoriaAsync();

        // El informe del VM coincide en sus KPIs con la salida directa del motor.
        var directo = ProyectoAuditorEngine.AuditarProyecto(proyecto);
        Assert.NotNull(vm.Informe);
        Assert.True(vm.HayInforme);
        Assert.False(vm.EjecutandoAnalisis);   // tras el await, la bandera vuelve a false
        Assert.Equal(directo.CantidadAdvertencias, vm.CantidadAdvertencias);
        Assert.Equal(directo.CantidadErrores, vm.CantidadErrores);
        Assert.Equal(directo.ProyectoConforme, vm.ProyectoConforme);
        Assert.Equal(directo.RatioMaximoGlobal, vm.RatioMaximoGlobal, precision: 6);
    }

    [Fact]
    public async Task FiltroGravedad_reduce_la_coleccion_expuesta()
    {
        // Proyecto con una columna huérfana (Id=5, sin zapata gemela) —
        // el motor emite al menos una advertencia CARGA-001 (ver
        // ProyectoAuditorEngineTests.ColumnaHuerfanaSinZapata_DetonaAlerta).
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var huerfana = new Columna { Id = 5, Nombre = "C-5" };
        huerfana.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0004 });
        huerfana.Acero.Capas.Add(new CapaAcero { Profundidad = 0.25, Area = 0.0004 });
        huerfana.Demandas.Add(new DemandaColumna { NombreCombo = "1.4D", Pu = 200.0, Mu = 5.0 });
        proyecto.Edificios[0].Niveles[0].Columnas.Add(huerfana);

        var vm = new AuditoriaViewModel(proyecto);
        await vm.ActualizarAuditoriaAsync();

        Assert.NotNull(vm.Informe);
        Assert.True(vm.Informe!.Mensajes.Count > 0,
            "Se esperaba al menos un mensaje (columna huérfana) en el informe.");

        // Sin filtro: la colección expuesta es exactamente la del informe.
        Assert.Null(vm.FiltroGravedad);
        Assert.Equal(vm.Informe.Mensajes.Count, vm.MensajesFiltrados.Count());

        // Con filtro = Advertencia: la colección queda con sólo los de ese
        // tipo (no necesariamente todos los del informe — si hubiera Info
        // o ErrorFallo, quedarían fuera).
        vm.FiltroGravedad = TipoMensajeAuditoria.Advertencia;
        var soloAdvertencias = vm.MensajesFiltrados.ToList();
        Assert.All(soloAdvertencias, m => Assert.Equal(TipoMensajeAuditoria.Advertencia, m.Tipo));
        Assert.True(soloAdvertencias.Count >= 1);

        // Con filtro = ErrorFallo: para un proyecto sin errores la colección
        // queda vacía aunque haya advertencias en el informe.
        vm.FiltroGravedad = TipoMensajeAuditoria.ErrorFallo;
        Assert.All(vm.MensajesFiltrados, m => Assert.Equal(TipoMensajeAuditoria.ErrorFallo, m.Tipo));
    }
}

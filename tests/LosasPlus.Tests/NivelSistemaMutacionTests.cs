using System.Linq;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.ViewModels;
using MemoriaPlus.Services;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Fase B1 (pérdida de datos — split-brain de niveles): las mutaciones de sistema
/// (<c>AgregarSistema</c> / <c>EliminarSistema</c>) deben operar sobre el
/// <c>NivelActivo</c>, no sobre la fachada de compatibilidad
/// <c>Proyecto.Sistemas</c> (que siempre apunta a <c>Niveles[0]</c>).
///
/// <para>
/// La UI LEE <c>NivelActivo.Sistemas</c> (MainWindow.axaml), pero antes del fix las
/// mutaciones ESCRIBÍAN en <c>_proyecto.Sistemas</c> = <c>Edificios[0].Niveles[0]</c>:
/// estando parado en el Nivel 2, «Agregar sistema» lo creaba en el Nivel 1.
/// Además el alias destructivo <c>Sistema</c> (<c>Clear()+Add()</c>) colapsaba la
/// colección del nivel activo a un solo sistema.
/// </para>
/// </summary>
public class NivelSistemaMutacionTests : System.IDisposable
{
    private readonly IMessageBoxService _msgBoxOriginal = AppServices.MessageBox;

    /// <summary>Restaura el servicio de diálogos real tras cada test (el stub es estático).</summary>
    public void Dispose() => AppServices.MessageBox = _msgBoxOriginal;

    /// <summary>Stub de confirmación fija para evitar el diálogo Avalonia real en tests.</summary>
    private sealed class StubMessageBox : IMessageBoxService
    {
        private readonly bool _confirma;
        public StubMessageBox(bool confirma) => _confirma = confirma;
        public Task<bool> ConfirmYesNoAsync(string title, string message) => Task.FromResult(_confirma);
        public Task InfoAsync(string title, string message) => Task.CompletedTask;
        public Task<ResultadoDescarte> ConfirmarGuardarDescartarCancelarAsync(string titulo, string mensaje)
            => Task.FromResult(ResultadoDescarte.Cancelar);
    }

    /// <summary>
    /// Construye un MainViewModel con DOS niveles, cada uno con su propio conjunto de
    /// sistemas, y deja activo el SEGUNDO nivel. Devuelve (vm, nivel1, nivel2).
    /// </summary>
    private static (MainViewModel vm, Nivel nivel1, Nivel nivel2) NuevoVmConDosNiveles()
    {
        var vm = new MainViewModel();
        var edificio = vm.Proyecto.Edificios[0];
        var nivel1 = edificio.Niveles[0];

        // Identifica el sistema del Nivel 1 con una losa marcadora.
        nivel1.Sistemas[0].Losas.Add(new Losa { Id = 101, Tipo = 10, Lx = 4, Ly = 5 });

        // Agrega y activa el Nivel 2 (AgregarNivel deja NivelActivo = nivel2).
        vm.AgregarNivel("Nivel 2", 3.0);
        var nivel2 = vm.NivelActivo!;

        Assert.NotSame(nivel1, nivel2);
        Assert.Same(nivel2, vm.NivelActivo);
        return (vm, nivel1, nivel2);
    }

    [Fact]
    public void AgregarSistema_agrega_al_nivel_activo_y_no_toca_el_nivel_uno()
    {
        var (vm, nivel1, nivel2) = NuevoVmConDosNiveles();

        var sistemasNivel1Antes = nivel1.Sistemas.Count;
        var sistemasNivel2Antes = nivel2.Sistemas.Count;

        var nuevo = vm.AgregarSistema("Sistema de prueba N2");

        // El nuevo sistema debe vivir en el Nivel 2 (el activo)…
        Assert.Contains(nuevo, nivel2.Sistemas);
        Assert.Equal(sistemasNivel2Antes + 1, nivel2.Sistemas.Count);

        // …y el Nivel 1 NO debe haber cambiado.
        Assert.DoesNotContain(nuevo, nivel1.Sistemas);
        Assert.Equal(sistemasNivel1Antes, nivel1.Sistemas.Count);

        // El sistema activo queda apuntando al recién creado.
        Assert.Same(nuevo, vm.SistemaActivo);
    }

    [Fact]
    public async Task EliminarSistema_elimina_del_nivel_activo_y_no_toca_el_nivel_uno()
    {
        AppServices.MessageBox = new StubMessageBox(confirma: true);
        var (vm, nivel1, nivel2) = NuevoVmConDosNiveles();

        // Asegura que el Nivel 2 tenga al menos 2 sistemas para poder eliminar uno.
        var aEliminar = vm.AgregarSistema("Sistema a eliminar");
        Assert.Contains(aEliminar, nivel2.Sistemas);

        var sistemasNivel1Antes = nivel1.Sistemas.Count;
        var sistemasNivel2Antes = nivel2.Sistemas.Count;

        await vm.EliminarSistema(aEliminar);

        // Eliminado del Nivel 2…
        Assert.DoesNotContain(aEliminar, nivel2.Sistemas);
        Assert.Equal(sistemasNivel2Antes - 1, nivel2.Sistemas.Count);

        // …Nivel 1 intacto.
        Assert.Equal(sistemasNivel1Antes, nivel1.Sistemas.Count);
    }

    [Fact]
    public void Mutaciones_no_colapsan_la_coleccion_del_nivel_activo_a_un_elemento()
    {
        var (vm, _, nivel2) = NuevoVmConDosNiveles();

        // Crea varios sistemas en el nivel activo.
        vm.AgregarSistema("S2");
        vm.AgregarSistema("S3");

        // El nivel activo debe conservar TODOS sus sistemas (no colapsar a 1).
        Assert.True(nivel2.Sistemas.Count >= 3,
            $"El Nivel 2 debería tener >= 3 sistemas, pero tiene {nivel2.Sistemas.Count} " +
            "(síntoma del alias destructivo Clear()+Add()).");
    }
}

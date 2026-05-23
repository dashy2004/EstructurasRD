using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.ViewModels.Viewport3D;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del <see cref="Viewport3DViewModel"/> (Fase 3D-I1 del Plan
/// Maestro de Expansión 3D). Validan el ciclo de vida del presentador 3D
/// (instanciación, regeneración con proyecto vacío, dispose) sin requerir
/// un <c>Application.Current</c> de WPF activo — el VM es tolerante al
/// ambiente headless de xUnit gracias a la guarda en
/// <see cref="SyncEscenaService"/> que omite el swap si no hay dispatcher.
/// </summary>
public class Viewport3DViewModelTests
{
    [Fact]
    public void Instanciacion_Limpia_Establece_Valores_Por_Defecto()
    {
        using var vm = new Viewport3DViewModel();

        Assert.NotNull(vm.EffectsManager);
        Assert.NotNull(vm.Camera);
        Assert.NotNull(vm.ItemsEscena3D);
        Assert.Empty(vm.ItemsEscena3D);
        Assert.False(vm.CargandoEscena);
    }

    [Fact]
    public async Task RegenerarEscenaAsync_Con_Proyecto_Vacio_No_Lanza_Excepcion()
    {
        using var vm = new Viewport3DViewModel();
        var proyecto = new Proyecto();   // sin edificios, sin elementos

        // El método debe completar sin excepción y dejar la escena vacía.
        await vm.RegenerarEscenaAsync(proyecto);

        Assert.Empty(vm.ItemsEscena3D);
        Assert.False(vm.CargandoEscena,
            "CargandoEscena debe quedar en false tras el finally del método.");
    }

    [Fact]
    public async Task RegenerarEscenaAsync_Con_Proyecto_Nulo_Es_NoOp_Seguro()
    {
        using var vm = new Viewport3DViewModel();

        // Caso defensivo: el shell durante construcción puede llamar antes
        // de tener el Proyecto instanciado. El VM debe tolerarlo.
        await vm.RegenerarEscenaAsync(null);

        Assert.Empty(vm.ItemsEscena3D);
        Assert.False(vm.CargandoEscena);
    }

    [Fact]
    public void Dispose_Libera_Recursos_Sin_Lanzar_Excepcion()
    {
        var vm = new Viewport3DViewModel();

        // Una llamada y luego otra para verificar idempotencia (defensa
        // contra doble cierre del Window).
        vm.Dispose();
        vm.Dispose();

        // Tras dispose, la colección queda vacía (referencias soltadas).
        Assert.Empty(vm.ItemsEscena3D);
    }

    [Fact]
    public async Task RegenerarEscenaAsync_Despues_De_Dispose_Es_NoOp()
    {
        var vm = new Viewport3DViewModel();
        vm.Dispose();

        // Llamadas posteriores a Dispose deben ser inocuas en lugar de
        // lanzar ObjectDisposedException — el shell podría disparar el
        // setter de ModoActivo durante el cierre de la ventana.
        await vm.RegenerarEscenaAsync(new Proyecto());

        Assert.Empty(vm.ItemsEscena3D);
    }
}

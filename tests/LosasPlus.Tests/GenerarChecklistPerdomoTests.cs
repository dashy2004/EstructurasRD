using System.Linq;
using LosasPlus.Models;
using MemoriaVm = MemoriaPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Fase C (sub-tarea 3): checklist real de "Verificaciones previas" + aviso si
/// faltan datos de F. Perdomo. Cobertura a nivel ViewModel de
/// <see cref="MemoriaVm.MainViewModel"/>: las propiedades del checklist reflejan
/// el estado real del proyecto y <c>HayDatosPerdomo</c> distingue la presencia
/// de la salida .txt importada.
/// </summary>
public class GenerarChecklistPerdomoTests
{
    [Fact]
    public void Proyecto_ejemplo_recien_creado_no_tiene_datos_Perdomo()
    {
        var vm = new MemoriaVm.MainViewModel();

        // El proyecto sembrado trae datos generales, cargas y niveles…
        Assert.True(vm.TieneNiveles);
        Assert.True(vm.TieneCargasConfiguradas);

        // …pero NINGÚN sistema tiene la salida .txt de F. Perdomo aún.
        Assert.False(vm.HayDatosPerdomo);
        Assert.All(vm.ProyectoActivo.Sistemas, s => Assert.False(s.TieneSalidaPerdomo));
    }

    [Fact]
    public void Importar_salida_Perdomo_a_un_sistema_activa_HayDatosPerdomo()
    {
        var vm = new MemoriaVm.MainViewModel();
        Assert.False(vm.HayDatosPerdomo);

        var sistema = vm.ProyectoActivo.Sistemas.First();
        sistema.SalidaPerdomo = new SalidaPerdomo { ArchivoTxt = "x.txt" };

        Assert.True(vm.HayDatosPerdomo);
        Assert.True(sistema.TieneSalidaPerdomo);

        // Quitarla vuelve a dejar el checklist en estado "falta Perdomo".
        sistema.SalidaPerdomo = null;
        Assert.False(vm.HayDatosPerdomo);
    }

    [Fact]
    public void TieneDatosGenerales_es_falso_si_se_vacian_los_campos_clave()
    {
        var vm = new MemoriaVm.MainViewModel();
        var p = vm.ProyectoActivo;

        // Vaciar todos los campos que cuentan como "datos generales".
        p.Nombre = "";
        p.Ciudad = "";
        p.Uso = "";
        p.UbicacionCompleta = "";

        Assert.False(vm.TieneDatosGenerales);

        p.Nombre = "Edificio Demo";
        Assert.True(vm.TieneDatosGenerales);
    }
}

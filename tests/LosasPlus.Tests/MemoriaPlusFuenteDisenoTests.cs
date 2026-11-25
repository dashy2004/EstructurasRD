using MemoriaPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

public class MemoriaPlusFuenteDisenoTests
{
    [Theory]
    [InlineData("{\"bloques\":[]}")]
    [InlineData("{\"fundaciones\":{}}")]
    public async Task Selecciona_un_informe_anterior_sin_listas_opcionales(string contenido)
    {
        var vm = new MainViewModel();
        var ruta = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(ruta, contenido);
            await vm.CargarDatosDisenoAsync(ruta);
            Assert.Equal(ruta, vm.FuenteDiseno);
        }
        finally { File.Delete(ruta); }
    }

    [Theory]
    [InlineData("{\"bloques\":{}}")]
    [InlineData("{\"fundaciones\":[]}")]
    [InlineData("{\"portada\":{}}")]
    [InlineData("{\"fundaciones\":{\"figuras\":[]}}")]
    [InlineData("{\"niveles\":[null]}")]
    public async Task Una_entrada_incompatible_conserva_la_fuente_anterior(string contenido)
    {
        var vm = new MainViewModel();
        string anterior = vm.FuenteDiseno;
        var ruta = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(ruta, contenido);
            await Assert.ThrowsAsync<InvalidDataException>(() => vm.CargarDatosDisenoAsync(ruta));
            Assert.Equal(anterior, vm.FuenteDiseno);
        }
        finally { File.Delete(ruta); }
    }

    [Theory]
    [InlineData("{\"bloques\":[],\"fundaciones\":{}}")]
    [InlineData("{\"fundaciones\":{\"presiones\":[]}}")]
    public async Task No_acepta_resultados_como_configuracion(string contenido)
    {
        var vm = new MainViewModel();
        var ruta = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(ruta, contenido);
            await Assert.ThrowsAsync<InvalidDataException>(() => vm.CargarConfiguracionDisenoAsync(ruta));
        }
        finally { File.Delete(ruta); }
    }

    [Fact]
    public void Proyecto_vacio_pide_una_fuente_antes_de_abrir_el_dialogo_de_salida()
    {
        var vm = new MainViewModel { ProyectoActivo = new LosasPlus.Models.Proyecto { Nombre = "" } };
        Assert.Equal("Elija un proyecto o un data.json", vm.FuenteDiseno);
        vm.GenerarDisenoCommand.Execute(null);
        Assert.Contains("Abra o cree un proyecto", vm.EstadoDiseno);
        Assert.Null(vm.UltimoDiseno);
    }
}
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LosasPlus.ViewModels;
using MemoriaPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Paso de LosasPlus a la memoria: lo que se importa o calcula debe quedar en el
/// proyecto que recibe el generador, y guardar ese proyecto nunca debe dañar el
/// archivo <c>.DL</c> de origen.
/// </summary>
[Collection("AppServices")]
public class PasoAMemoriaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "losasplus-" + Guid.NewGuid().ToString("N"));
    private readonly IDialogService _dialogosPrevios = AppServices.Dialogs;

    public PasoAMemoriaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        AppServices.Dialogs = _dialogosPrevios;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* limpieza de prueba */ }
    }

    private string CopiarFixture(string nombre)
    {
        var destino = Path.Combine(_dir, nombre);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", nombre), destino);
        return destino;
    }

    private sealed class DialogosFalsos : IDialogService
    {
        private readonly string? _rutaGuardar;
        public int VecesGuardar { get; private set; }
        public DialogosFalsos(string? rutaGuardar) => _rutaGuardar = rutaGuardar;
        public Task<string?> OpenFileAsync(string title, params FileFilter[] filters) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string suggestedName, string defaultExt, params FileFilter[] filters)
        {
            VecesGuardar++;
            return Task.FromResult(_rutaGuardar);
        }
    }

    [Fact]
    public async Task Importar_el_txt_deja_momentos_y_armaduras_disponibles_para_la_memoria()
    {
        var vm = new MainViewModel();
        vm.AbrirDL(CopiarFixture("restaurante_2.DL"));

        await vm.ImportarTxtAsync(CopiarFixture("restaurante_2.TXT"));

        var salida = vm.Sistema.SalidaPerdomo;
        Assert.NotNull(salida);
        Assert.Equal(5, salida!.Momentos.Count);
        Assert.Equal(5, salida.ArmadurasXCentro.Count);
        Assert.Equal(2, salida.ArmadurasXApoyos.Count);
        Assert.Equal(4, salida.ArmadurasYApoyos.Count);
    }

    [Theory]
    [InlineData("C:/obra/proyecto.lpx.json", true)]
    [InlineData("C:/obra/PROYECTO.JSON", true)]
    [InlineData("C:/obra/sistema.DL", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Solo_un_json_se_reconoce_como_archivo_de_proyecto(string? ruta, bool esperado)
    {
        Assert.Equal(esperado, MainViewModel.EsArchivoDeProyecto(ruta));
    }

    [Fact]
    public async Task Guardar_un_proyecto_abierto_desde_un_DL_pide_destino_y_no_toca_el_DL()
    {
        var dl = CopiarFixture("restaurante_2.DL");
        var original = File.ReadAllBytes(dl);
        var destino = Path.Combine(_dir, "proyecto.lpx.json");
        var dialogos = new DialogosFalsos(destino);
        AppServices.Dialogs = dialogos;
        var vm = new MainViewModel();
        vm.AbrirDL(dl);

        bool guardado = await vm.GuardarAsync();

        Assert.True(guardado);
        Assert.Equal(1, dialogos.VecesGuardar);
        Assert.Equal(original, File.ReadAllBytes(dl));
        Assert.True(File.Exists(destino));
    }

    [Fact]
    public async Task Generar_memoria_espera_a_que_el_proyecto_quede_guardado_antes_de_decidir()
    {
        var destino = Path.Combine(_dir, "nuevo.lpx.json");
        AppServices.Dialogs = new DialogosFalsos(destino);
        string? archivoLanzado = null;
        var vm = new MainViewModel { LanzadorMemoria = (_, archivo) => archivoLanzado = archivo };

        await vm.GenerarMemoriaAsync();

        Assert.True(File.Exists(destino));
        Assert.DoesNotContain("Cancelado", vm.StatusGeneracion);
        Assert.True(archivoLanzado is null || archivoLanzado == destino);
    }

    [Fact]
    public async Task Generar_memoria_se_cancela_si_el_usuario_no_elige_donde_guardar()
    {
        AppServices.Dialogs = new DialogosFalsos(null);
        bool lanzado = false;
        var vm = new MainViewModel { LanzadorMemoria = (_, _) => lanzado = true };

        await vm.GenerarMemoriaAsync();

        Assert.Contains("Cancelado", vm.StatusGeneracion);
        Assert.False(lanzado);
    }

    [Fact]
    public void El_ejecutable_de_memoria_se_busca_tambien_en_la_salida_net8_de_desarrollo()
    {
        var raiz = Path.Combine(_dir, "repo");
        var baseDir = Path.Combine(raiz, "src", "bin", "Debug", "net8.0");
        var exe = Path.Combine(raiz, "src.Memoria", "bin", "Debug", "net8.0", "MemoriaPlus.exe");

        var candidatas = MainViewModel.RutasCandidatasMemoriaPlus(baseDir).Select(Path.GetFullPath).ToList();

        Assert.Contains(Path.GetFullPath(exe), candidatas);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "MemoriaPlus.exe")), candidatas[0]);
    }
}

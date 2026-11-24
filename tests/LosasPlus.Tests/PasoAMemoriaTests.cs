using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LosasPlus.Generation;
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
        var vm = new MainViewModel
        {
            CarpetaMotorMemoria = () => null,
            LanzadorMemoria = (_, archivo) => archivoLanzado = archivo,
        };

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

    private sealed class MotorFalso
    {
        private readonly ResultadoMotorMemoria _resultado;
        public string? Data { get; private set; }
        public string? Docx { get; private set; }
        public string? Config { get; private set; }
        public string? DatosVistos { get; private set; }
        public string? ConfigVista { get; private set; }
        public MotorFalso(bool ok, string mensaje = "") => _resultado = new ResultadoMotorMemoria(ok, mensaje);

        public Task<ResultadoMotorMemoria> Correr(string carpeta, string data, string docx, string trabajo, string config)
        {
            (Data, Docx, Config) = (data, docx, config);
            DatosVistos = File.ReadAllText(data);
            ConfigVista = File.ReadAllText(config);
            return Task.FromResult(_resultado);
        }
    }

    private MainViewModel VmConProyectoCalculado(MotorFalso motor, Action<string> abrir)
    {
        AppServices.Dialogs = new DialogosFalsos(Path.Combine(_dir, "obra.lpx.json"));
        var vm = new MainViewModel
        {
            CarpetaMotorMemoria = () => Path.Combine(_dir, "motor"),
            EjecutorMotorMemoria = motor.Correr,
            AbridorDeDocumentos = abrir,
            LanzadorMemoria = (_, _) => throw new InvalidOperationException("no debe abrir MemoriaPlus"),
        };
        vm.AbrirDL(CopiarFixture("restaurante_2.DL"));
        vm.CalcularNativo();
        return vm;
    }

    [Fact]
    public async Task Con_el_motor_instalado_generar_memoria_le_pasa_las_losas_calculadas_y_abre_el_documento()
    {
        var motor = new MotorFalso(ok: true);
        string? abierto = null;
        var vm = VmConProyectoCalculado(motor, ruta => abierto = ruta);

        await vm.GenerarMemoriaAsync();

        var datos = System.Text.Json.JsonDocument.Parse(motor.DatosVistos!).RootElement;
        Assert.Equal(5, datos.GetProperty("niveles")[0].GetProperty("mom").EnumerateObject().Count());
        Assert.Equal(Path.Combine(_dir, "obra-memoria", "data.json"), motor.Data);
        Assert.EndsWith(".docx", motor.Docx);
        Assert.Equal(motor.Docx, abierto);
        Assert.Contains("Memoria generada", vm.StatusGeneracion);
    }

    [Fact]
    public async Task La_portada_que_el_usuario_edito_no_se_pisa_al_regenerar()
    {
        var motor = new MotorFalso(ok: true);
        var vm = VmConProyectoCalculado(motor, _ => { });
        Directory.CreateDirectory(Path.Combine(_dir, "obra-memoria"));
        File.WriteAllText(Path.Combine(_dir, "obra-memoria", "memoria.json"), "{\"portada\":{\"titulo\":\"Editado a mano\"}}");

        await vm.GenerarMemoriaAsync();

        Assert.Contains("Editado a mano", motor.ConfigVista);
    }

    [Fact]
    public async Task Si_el_motor_falla_se_muestra_el_motivo_y_no_se_abre_nada()
    {
        var motor = new MotorFalso(ok: false, "ModuleNotFoundError: No module named 'docx'");
        bool abierto = false;
        var vm = VmConProyectoCalculado(motor, _ => abierto = true);

        await vm.GenerarMemoriaAsync();

        Assert.Contains("No module named 'docx'", vm.StatusGeneracion);
        Assert.False(abierto);
    }

    /// <summary>
    /// Prueba de punta a punta con el motor real. Solo comprueba algo en una
    /// máquina con el motor instalado (variable MEMORIA_DISENO_DIR); en las demás
    /// termina sin afirmar nada, y deja constancia en la salida de la prueba.
    /// </summary>
    [Fact]
    public async Task Con_el_motor_real_instalado_se_produce_el_documento_de_Word()
    {
        string? carpeta = MotorMemoriaDiseno.ResolverCarpeta(AppContext.BaseDirectory);
        if (carpeta is null)
        {
            Console.WriteLine("Motor memoria-diseno no instalado: prueba de punta a punta omitida.");
            return;
        }
        AppServices.Dialogs = new DialogosFalsos(Path.Combine(_dir, "obra.lpx.json"));
        string? abierto = null;
        var vm = new MainViewModel { AbridorDeDocumentos = ruta => abierto = ruta };
        vm.AbrirDL(CopiarFixture("restaurante_2.DL"));
        vm.CalcularNativo();

        await vm.GenerarMemoriaAsync();

        Assert.Contains("Memoria generada", vm.StatusGeneracion);
        Assert.NotNull(abierto);
        Assert.True(new FileInfo(abierto!).Length > 10_000);
        string copia = Environment.GetEnvironmentVariable("MEMORIA_DISENO_COPIA") ?? "";
        if (copia.Length > 0) File.Copy(abierto!, copia, overwrite: true);
    }

    [Fact]
    public void El_motor_se_busca_primero_en_la_variable_de_entorno_y_luego_junto_a_la_aplicacion()
    {
        var baseDir = Path.Combine(_dir, "app", "bin");

        var candidatas = MotorMemoriaDiseno.CarpetasCandidatas(baseDir, @"D:\motor").ToList();

        Assert.Equal(@"D:\motor", candidatas[0]);
        Assert.Contains(Path.Combine(_dir, "app", "memoria-diseno"), candidatas);
    }

    [Fact]
    public void Sin_guion_generar_py_la_carpeta_no_cuenta_como_motor_instalado()
    {
        var conGuion = Path.Combine(_dir, "con");
        Directory.CreateDirectory(conGuion);
        File.WriteAllText(Path.Combine(conGuion, "generar.py"), "");

        Assert.Equal(conGuion, MotorMemoriaDiseno.ResolverCarpeta(_dir, conGuion));
        Assert.Null(MotorMemoriaDiseno.ResolverCarpeta(Path.Combine(_dir, "nada"), Path.Combine(_dir, "sin")));
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

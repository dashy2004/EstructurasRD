using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using LosasPlus.Generation;
using MemoriaPlus.Common;
using MemoriaPlus.Services;

namespace MemoriaPlus.ViewModels;

public sealed partial class MainViewModel
{
    public AsyncRelayCommand GenerarDisenoCommand { get; }
    public AsyncRelayCommand SeleccionarDatosDisenoCommand { get; }
    public AsyncRelayCommand SeleccionarConfiguracionDisenoCommand { get; }
    public RelayCommand UsarProyectoDisenoCommand { get; }
    public RelayCommand AbrirDisenoCommand { get; }

    private string? _datosDiseno;
    private string? _configuracionDiseno;
    private bool FaltaProyectoDiseno => string.IsNullOrWhiteSpace(ProyectoActivo?.Nombre)
        && string.IsNullOrWhiteSpace(ProyectoActivo?.Archivo)
        && ProyectoActivo?.Edificios.SelectMany(e => e.Niveles).SelectMany(n => n.Sistemas).Any() != true;
    public string FuenteDiseno => _datosDiseno ?? (FaltaProyectoDiseno
        ? "Elija un proyecto o un data.json" : $"Proyecto activo · {ProyectoActivo?.Nombre}");
    public string ConfiguracionDiseno => _configuracionDiseno ??
        (_datosDiseno is null && FaltaProyectoDiseno ? "Sin configurar" :
         _datosDiseno is null ? "Datos de portada del proyecto activo" : "Configuración inicial del motor");
    public string EstadoDiseno { get; private set; } = "";
    public string? UltimoDiseno { get; private set; }
    public bool GenerandoDiseno { get; private set; }

    private void MostrarEstadoDiseno(string texto)
    {
        EstadoDiseno = texto;
        OnPropertyChanged(nameof(EstadoDiseno));
    }

    private void CambiarFuenteDiseno(string? datos, string? configuracion)
    {
        _datosDiseno = datos;
        _configuracionDiseno = configuracion;
        UltimoDiseno = null;
        OnPropertyChanged(nameof(FuenteDiseno));
        OnPropertyChanged(nameof(ConfiguracionDiseno));
        OnPropertyChanged(nameof(UltimoDiseno));
        AbrirDisenoCommand.RaiseCanExecuteChanged();
        MostrarEstadoDiseno("");
    }

    private void RestablecerFuenteDiseno() => CambiarFuenteDiseno(null, null);

    private async Task SeleccionarEntradaAsync(bool datos)
    {
        try
        {
            string? ruta = await AppServices.Dialogs.OpenFileAsync(
                datos ? "Seleccionar datos de diseño (data.json)" : "Seleccionar configuración (memoria.json)",
                new FileFilter("JSON", new[] { "*.json" }));
            if (ruta is null) return;
            if (datos) await CargarDatosDisenoAsync(ruta);
            else await CargarConfiguracionDisenoAsync(ruta);
        }
        catch (Exception ex)
        {
            MostrarEstadoDiseno($"No se pudo cargar la entrada: {ex.Message}");
        }
    }

    public async Task CargarDatosDisenoAsync(string ruta)
    {
        ruta = Path.GetFullPath(ruta);
        await ValidarEntradaAsync(ruta, true);
        ActivarDatosDiseno(ruta);
    }

    private void ActivarDatosDiseno(string ruta)
    {
        string hermana = Path.Combine(Path.GetDirectoryName(ruta)!, "memoria.json");
        CambiarFuenteDiseno(ruta, File.Exists(hermana) ? hermana : null);
    }

    public async Task CargarConfiguracionDisenoAsync(string ruta)
    {
        ruta = Path.GetFullPath(ruta);
        await ValidarEntradaAsync(ruta, false);
        CambiarFuenteDiseno(_datosDiseno, ruta);
    }

    private static async Task ValidarEntradaAsync(string ruta, bool datos)
    {
        using var archivo = File.OpenRead(ruta);
        using var json = await JsonDocument.ParseAsync(archivo);
        var raiz = json.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Se esperaba un objeto JSON.");
        var listas = new[] { "niveles", "bloques", "sobrecarga", "avisos" };
        bool tieneFundaciones = raiz.TryGetProperty("fundaciones", out var fundaciones);
        if (tieneFundaciones && fundaciones.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("La sección «fundaciones» debe ser un objeto.");
        if (datos)
        {
            if (raiz.TryGetProperty("portada", out _) || (tieneFundaciones &&
                new[] { "texto", "figuras", "figuras_del_pdf", "esfuerzo_admisible_ton_m2", "combinaciones" }
                    .Any(nombre => fundaciones.TryGetProperty(nombre, out _))))
                throw new InvalidDataException("El archivo contiene la configuración de memoria.json. Seleccione data.json.");
            bool tieneSeccion = tieneFundaciones;
            // generar.py completa las listas ausentes en versiones anteriores del contrato.
            foreach (string nombre in listas)
            {
                if (!raiz.TryGetProperty(nombre, out var valor)) continue;
                if (valor.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"La sección «{nombre}» debe ser una lista.");
                var tipo = nombre == "avisos" ? JsonValueKind.String : JsonValueKind.Object;
                if (valor.EnumerateArray().Any(fila => fila.ValueKind != tipo))
                    throw new InvalidDataException($"La sección «{nombre}» contiene filas incompatibles.");
                tieneSeccion = true;
            }
            if (!tieneSeccion)
                throw new InvalidDataException("El archivo no contiene secciones de data.json.");
        }
        else
        {
            if (listas.Any(nombre => raiz.TryGetProperty(nombre, out _)) || (tieneFundaciones &&
                new[] { "modelo", "presiones", "punzonado", "resortes", "losas", "franjas", "archivo" }
                    .Any(nombre => fundaciones.TryGetProperty(nombre, out _))))
                throw new InvalidDataException("El archivo contiene resultados de data.json. Seleccione memoria.json.");
            if (raiz.EnumerateObject().Any() && !tieneFundaciones &&
                !new[] { "_ayuda", "portada", "responsable", "control", "revisiones", "firmas", "proyecto",
                         "materiales", "normas", "vistas_bloque", "anexo_figuras", "observaciones_extra",
                         "observaciones_omitir", "checklist_mivhed" }.Any(nombre => raiz.TryGetProperty(nombre, out _)))
                throw new InvalidDataException("El archivo no contiene campos de configuración de memoria.json.");
        }
    }

    private void IndicarGeneracion(bool activa)
    {
        GenerandoDiseno = activa;
        ActualizarComandosFuentes();
        GenerarMemoriaCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(GenerandoDiseno));
        GenerarDisenoCommand.RaiseCanExecuteChanged();
        SeleccionarDatosDisenoCommand.RaiseCanExecuteChanged();
        SeleccionarConfiguracionDisenoCommand.RaiseCanExecuteChanged();
        UsarProyectoDisenoCommand.RaiseCanExecuteChanged();
        CargarGitHubCommand.RaiseCanExecuteChanged();
        ExportarChatGPTCommand.RaiseCanExecuteChanged();
    }

    private async Task GenerarDisenoAsync()
    {
        var proyecto = ProyectoActivo;
        string? datos = _datosDiseno, config = _configuracionDiseno;
        bool desdeProyecto = datos is null;
        IndicarGeneracion(true);
        string? provisional = null;
        DirectoryInfo? trabajo = null;
        try
        {
            if (desdeProyecto && FaltaProyectoDiseno)
                throw new InvalidDataException("Abra o cree un proyecto, o elija un data.json con los resultados de diseño.");
            string motor = MotorMemoriaDiseno.ResolverCarpeta(AppContext.BaseDirectory) ??
                throw new DirectoryNotFoundException("Indique la carpeta del motor en MEMORIA_DISENO_DIR y reinicie MemoriaPlus.");
            if (datos != null) await ValidarEntradaAsync(datos, true);
            if (config != null) await ValidarEntradaAsync(config, false);
            string? destino = await AppServices.Dialogs.SaveFileAsync(
                "Guardar memoria de diseño", datos is null ? SugerirNombreArchivo() : "Memoria de diseño.docx",
                ".docx", new FileFilter("Documento Word", new[] { "*.docx" }));
            if (destino is null) return;
            destino = Path.GetFullPath(destino);
            if (!Path.GetExtension(destino).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El destino debe tener extensión .docx.");

            MostrarEstadoDiseno("Generando el documento Word…");
            trabajo = Directory.CreateTempSubdirectory("memoriaplus-");
            string dirFiguras = Path.GetDirectoryName(config ?? datos ?? destino)!;
            if (datos is null)
            {
                datos = Path.Combine(trabajo.FullName, "data.json");
                await File.WriteAllTextAsync(datos, MemoriaDisenoExporter.ExportarDatos(proyecto));
            }
            if (config is null)
            {
                config = Path.Combine(trabajo.FullName, "memoria.json");
                // Para un data.json externo el motor crea su propia configuración.
                if (desdeProyecto)
                    await File.WriteAllTextAsync(config, MemoriaDisenoExporter.ConfiguracionInicial(proyecto));
            }

            // El motor elimina cualquier DOCX previo en su destino. Solo recibe esta ruta nueva,
            // en el volumen del destino final, para permitir una sustitución después de verificar.
            provisional = Path.Combine(Path.GetDirectoryName(destino)!, $".memoriaplus-{Guid.NewGuid():N}.docx");
            var resultado = await MotorMemoriaDiseno.GenerarAsync(motor, datos, provisional, trabajo.FullName,
                config, Path.Combine(dirFiguras, "imagenes"));
            if (!resultado.Ok) throw new InvalidDataException(resultado.Mensaje);
            using (var word = WordprocessingDocument.Open(provisional, false))
                if (word.MainDocumentPart?.Document.Body is null)
                    throw new InvalidDataException("El documento generado carece de cuerpo de Word.");
            File.Move(provisional, destino, overwrite: true);
            provisional = null;
            if (ReferenceEquals(proyecto, ProyectoActivo))
            {
                UltimoDiseno = destino;
                OnPropertyChanged(nameof(UltimoDiseno));
                AbrirDisenoCommand.RaiseCanExecuteChanged();
                MostrarEstadoDiseno(string.IsNullOrWhiteSpace(resultado.Avisos) ? "Memoria de diseño generada."
                    : "Memoria de diseño generada." + Environment.NewLine + resultado.Avisos);
            }
        }
        catch (Exception ex)
        {
            MostrarEstadoDiseno($"No se pudo generar la memoria: {ex.Message}");
        }
        finally
        {
            try
            {
                if (provisional != null) File.Delete(provisional);
                trabajo?.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MostrarEstadoDiseno(EstadoDiseno + " No se pudieron retirar todos los archivos temporales.");
            }
            IndicarGeneracion(false);
        }
    }

    private void AbrirDiseno()
    {
        if (UltimoDiseno is null) return;
        try { AppServices.Launcher.OpenInDefaultApp(UltimoDiseno); }
        catch (Exception ex) { MostrarEstadoDiseno($"No se pudo abrir el documento: {ex.Message}"); }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Generation;
using MemoriaPlus.Common;
using MemoriaPlus.Services;

namespace MemoriaPlus.ViewModels;

public sealed partial class MainViewModel
{
    private readonly SeleccionFuentesMemoria _seleccion = new();
    public AsyncRelayCommand AgregarFuenteDisenoCommand { get; }
    public RelayCommand LimpiarFuentesDisenoCommand { get; }
    public AsyncRelayCommand ExtraerDatosDisenoCommand { get; }
    public RelayCommand CancelarExtraccionCommand { get; }
    private CancellationTokenSource? _cancelacionExtraccion;
    public bool ExtraccionEnCurso => _cancelacionExtraccion != null;

    private void IndicarExtraccion(CancellationTokenSource? cancelacion)
    {
        _cancelacionExtraccion = cancelacion;
        OnPropertyChanged(nameof(ExtraccionEnCurso));
        CancelarExtraccionCommand.RaiseCanExecuteChanged();
    }

    private void CancelarExtraccion()
    {
        _cancelacionExtraccion?.Cancel();
        CancelarExtraccionCommand.RaiseCanExecuteChanged();
    }

    public string FuentesExtraccion => string.Join(Environment.NewLine, _seleccion.Archivos);
    public string EstadoExtraccion { get; private set; } = "";

    private void EstadoDeExtraccion(string mensaje)
    {
        EstadoExtraccion = mensaje;
        OnPropertyChanged(nameof(EstadoExtraccion));
    }

    private void ActualizarComandosFuentes()
    {
        AgregarFuenteDisenoCommand.RaiseCanExecuteChanged();
        LimpiarFuentesDisenoCommand.RaiseCanExecuteChanged();
        ExtraerDatosDisenoCommand.RaiseCanExecuteChanged();
    }

    private void LimpiarFuentesDiseno()
    {
        _seleccion.Limpiar();
        OnPropertyChanged(nameof(FuentesExtraccion));
        EstadoDeExtraccion("");
        ActualizarComandosFuentes();
    }

    private async Task AgregarFuenteDisenoAsync()
    {
        var proyecto = ProyectoActivo;
        try
        {
            string? ruta = await AppServices.Dialogs.OpenFileAsync("Agregar una fuente de cálculo",
                new FileFilter("Modelos, libros de cargas, listados o manifiesto", new[] { "*.edb", "*.fdb", "*.xls", "*.xlsx", "*.xlsm", "*.txt", "*.dl", "*.json" }));
            if (ruta == null || !ReferenceEquals(proyecto, ProyectoActivo) || GenerandoDiseno) return;
            _seleccion.Agregar(ruta);
            OnPropertyChanged(nameof(FuentesExtraccion));
            EstadoDeExtraccion("");
            ActualizarComandosFuentes();
        }
        catch (Exception ex)
        {
            EstadoDeExtraccion("No se pudo agregar la fuente. " + ex.Message);
        }
    }

    private async Task ExtraerDatosDisenoAsync()
    {
        var proyecto = ProyectoActivo;
        IndicarGeneracion(true);
        DirectoryInfo? temporal = null;
        string? provisional = null;
        using var cancelacion = new CancellationTokenSource();
        bool incluyeModeloNativo = false;
        try
        {
            string? salida = await AppServices.Dialogs.SaveFileAsync("Guardar resultados de extracción", "data.json", ".json",
                new FileFilter("Datos de la memoria", new[] { "*.json" }));
            if (salida is null || !ReferenceEquals(proyecto, ProyectoActivo)) return;
            salida = Path.GetFullPath(salida);
            var manifiesto = _seleccion.Preparar(salida);
            string? motor = MotorMemoriaDiseno.ResolverCarpeta(AppContext.BaseDirectory);
            if (motor == null || !File.Exists(Path.Combine(motor, "armar_datos.py")))
                throw new FileNotFoundException("El motor instalado debe incluir armar_datos.py y sus lectores de fuentes.");

            incluyeModeloNativo = manifiesto["fundaciones"] != null || manifiesto["bloques"]?.AsArray().Count > 0;
            IndicarExtraccion(cancelacion);
            EstadoDeExtraccion("Extrayendo resultados de los archivos seleccionados…");
            temporal = Directory.CreateTempSubdirectory("memoriaplus-fuentes-");
            string man = Path.Combine(temporal.FullName, "fuentes.json");
            await File.WriteAllTextAsync(man, manifiesto.ToJsonString());
            provisional = Path.Combine(Path.GetDirectoryName(salida)!, $".extraccion-{Guid.NewGuid():N}.json");
            var info = new ProcessStartInfo(MotorMemoriaDiseno.ResolverPython(motor))
            {
                WorkingDirectory = temporal.FullName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[] { "-X", "utf8", Path.Combine(motor, "armar_datos.py"), man, provisional })
                info.ArgumentList.Add(arg);
            cancelacion.Token.ThrowIfCancellationRequested();
            using var proceso = Process.Start(info) ?? throw new IOException("No se pudo iniciar el lector de fuentes.");
            var stdout = proceso.StandardOutput.ReadToEndAsync(cancelacion.Token);
            var stderr = proceso.StandardError.ReadToEndAsync(cancelacion.Token);
            try
            {
                // Una instancia CSI independiente puede conservar una tubería. La cancelación
                // abarca también las lecturas y no espera indefinidamente el cierre de esa instancia.
                await Task.WhenAll(stdout, stderr, proceso.WaitForExitAsync(cancelacion.Token)).WaitAsync(cancelacion.Token);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                if (!proceso.HasExited)
                {
                    try { await DetenerLectorAsync(proceso); }
                    catch (InvalidOperationException) when (proceso.HasExited) { }
                    if (!await Task.Run(() => proceso.WaitForExit(5000)))
                        throw new IOException("No se pudo confirmar el cierre del lector de esta extracción.");
                }
                throw;
            }
            string errores = (await stderr).Trim();
            if (proceso.ExitCode != 0)
                throw new InvalidDataException(errores.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l))
                    ?? $"El lector terminó con el código {proceso.ExitCode}.");

            await ValidarEntradaAsync(provisional, true);
            cancelacion.Token.ThrowIfCancellationRequested();
            File.Move(provisional, salida, overwrite: true);
            provisional = null;
            if (!ReferenceEquals(proyecto, ProyectoActivo)) return;
            ActivarDatosDiseno(salida);
            var avisos = errores.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("AVISO:", StringComparison.Ordinal));
            EstadoDeExtraccion(string.Join(Environment.NewLine, new[] { $"Datos extraídos. {salida}" }.Concat(avisos)));
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            if (ReferenceEquals(proyecto, ProyectoActivo))
                EstadoDeExtraccion(incluyeModeloNativo
                    ? "Extracción cancelada. Si la instancia de ETABS o SAFE de esta extracción sigue abierta, ciérrela desde su ventana."
                    : "Extracción cancelada.");
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(proyecto, ProyectoActivo)) EstadoDeExtraccion("No se pudo completar la extracción. " + ex.Message);
        }
        finally
        {
            try
            {
                if (provisional != null) File.Delete(provisional);
                temporal?.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                EstadoDeExtraccion(EstadoExtraccion + " Quedaron archivos de trabajo en la carpeta temporal.");
            }
            IndicarExtraccion(null);
            IndicarGeneracion(false);
        }
    }

    private static async Task DetenerLectorAsync(Process proceso)
    {
        if (OperatingSystem.IsWindows())
        {
            // El ejecutable de un venv puede ser un lanzador con varios descendientes.
            // taskkill obtiene el árbol antes de terminar a los padres. El PID procede
            // exclusivamente del proceso que arrancó esta extracción.
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/pid", proceso.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "/t", "/f" }
            };
            using var cierre = Process.Start(info) ?? throw new IOException("No se pudo detener el lector.");
            using var plazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await cierre.WaitForExitAsync(plazo.Token); }
            catch (OperationCanceledException) { if (!cierre.HasExited) cierre.Kill(); }
            if (proceso.HasExited) return;
        }
        proceso.Kill(entireProcessTree: true);
    }
}

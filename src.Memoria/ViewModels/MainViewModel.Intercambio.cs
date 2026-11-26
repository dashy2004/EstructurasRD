using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Generation;
using MemoriaPlus.Common;
using MemoriaPlus.Services;

namespace MemoriaPlus.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly HttpClient GitHubPublico = new(new HttpClientHandler {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
    });
    private const int LimiteDescarga = 32 * 1024 * 1024;
    private string _enlaceGitHub = "";
    private string? _origenDescargado, _huellaDescargada;
    public string EnlaceGitHub
    {
        get => _enlaceGitHub;
        set { _enlaceGitHub = value; OnPropertyChanged(); }
    }
    public string EstadoIntercambio { get; private set; } = "";
    public AsyncRelayCommand CargarGitHubCommand { get; }
    public AsyncRelayCommand ExportarChatGPTCommand { get; }

    private void EstadoDeIntercambio(string mensaje)
    {
        EstadoIntercambio = mensaje;
        OnPropertyChanged(nameof(EstadoIntercambio));
    }

    public static Uri ArchivoPublicoGitHub(string enlace)
    {
        if (!Uri.TryCreate(enlace?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0 || uri.Query is not ("" or "?raw=1" or "?plain=1" or "?raw=true"))
            throw new InvalidDataException("Use un enlace HTTPS al archivo JSON público, sin credenciales.");
        var partes = uri.AbsolutePath.TrimStart('/').Split('/');
        if (uri.Host == "github.com" && partes.Length >= 5 && partes[2] == "blob")
            partes = partes.Where((_, i) => i != 2).ToArray();
        else if (uri.Host != "raw.githubusercontent.com")
            throw new InvalidDataException("En GitHub, copie el enlace del archivo o su enlace Raw.");
        if (partes.Length < 4 || partes.Any(p => p.Length == 0) ||
            !partes[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Seleccione un data.json público con su repositorio, revisión y ruta.");
        return new Uri("https://raw.githubusercontent.com/" + string.Join("/", partes));
    }

    private async Task CargarGitHubAsync()
    {
        var proyecto = ProyectoActivo;
        string? temporal = null;
        IndicarGeneracion(true);
        try
        {
            var origen = ArchivoPublicoGitHub(EnlaceGitHub);
            var destino = await AppServices.Dialogs.SaveFileAsync("Guardar copia local del data.json público",
                "datos-github.json", ".json", new FileFilter("Datos JSON", new[] { "*.json" }));
            if (destino == null) { EstadoDeIntercambio("Descarga cancelada."); return; }
            destino = Path.GetFullPath(destino);
            SeleccionFuentesMemoria.ValidarDestinoDatos(destino);
            EstadoDeIntercambio("Leyendo datos públicos de GitHub…");
            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var respuesta = await GitHubPublico.GetAsync(origen, HttpCompletionOption.ResponseHeadersRead, limite.Token);
            if (respuesta.StatusCode != HttpStatusCode.OK)
                throw new InvalidDataException($"GitHub devolvió {(int)respuesta.StatusCode}. Compruebe que el archivo sea público.");
            if (respuesta.Content.Headers.ContentLength > LimiteDescarga)
                throw new InvalidDataException("El JSON supera 32 MiB.");
            temporal = Path.Combine(Path.GetDirectoryName(destino)!, $".{Guid.NewGuid():N}.tmp");
            await using (var salida = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var entrada = await respuesta.Content.ReadAsStreamAsync(limite.Token))
            {
                byte[] buffer = new byte[81920];
                int leidos;
                while ((leidos = await entrada.ReadAsync(buffer, limite.Token)) > 0)
                {
                    if (salida.Length + leidos > LimiteDescarga) throw new InvalidDataException("El JSON supera 32 MiB.");
                    await salida.WriteAsync(buffer.AsMemory(0, leidos), limite.Token);
                }
            }
            await ValidarEntradaAsync(temporal, true);
            string huella;
            await using (var entrada = File.OpenRead(temporal))
                huella = Convert.ToHexString(await SHA256.HashDataAsync(entrada));
            File.Move(temporal, destino, overwrite: true);
            temporal = null;
            if (ReferenceEquals(proyecto, ProyectoActivo))
            {
                CambiarFuenteDiseno(destino, null);
                _origenDescargado = origen.AbsoluteUri;
                _huellaDescargada = huella;
            }
            EstadoDeIntercambio("Copia validada y guardada en " + destino);
        }
        catch (Exception ex) { EstadoDeIntercambio("No se pudo importar de GitHub. " + ex.Message); }
        finally
        {
            try { if (temporal != null) File.Delete(temporal); }
            catch (Exception limpieza) when (limpieza is IOException or UnauthorizedAccessException) { EstadoDeIntercambio(EstadoIntercambio + " Quedó un archivo temporal."); }
            finally { IndicarGeneracion(false); }
        }
    }

    private async Task ExportarChatGPTAsync()
    {
        string? temporal = null;
        string? archivoDatos = _datosDiseno, origen = _origenDescargado, huellaOriginal = _huellaDescargada;
        IndicarGeneracion(true);
        try
        {
            if (archivoDatos == null && FaltaProyectoDiseno)
                throw new InvalidDataException("Seleccione un proyecto o extraiga sus datos primero.");
            byte[] datos = archivoDatos == null
                ? Encoding.UTF8.GetBytes(MemoriaDisenoExporter.ExportarDatos(ProyectoActivo))
                : await File.ReadAllBytesAsync(archivoDatos);
            using var json = JsonDocument.Parse(datos.AsMemory(datos.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? 3 : 0));
            string huella = Convert.ToHexString(SHA256.HashData(datos));
            string? destino = await AppServices.Dialogs.SaveFileAsync("Preparar datos para revisar antes de adjuntar",
                "Revision-memoria.md", ".md", new FileFilter("Documento Markdown", new[] { "*.md" }));
            if (destino == null) return;
            destino = Path.GetFullPath(destino);
            if (!string.Equals(Path.GetExtension(destino), ".md", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Seleccione un archivo con extensión .md.");
            temporal = Path.Combine(Path.GetDirectoryName(destino)!, $".{Guid.NewGuid():N}.tmp");
            await using (var escritor = new StreamWriter(temporal, false, new UTF8Encoding(false)))
            {
                await escritor.WriteLineAsync("# Revisión de los datos de la memoria de diseño");
                await escritor.WriteLineAsync("\nComprueba qué capítulos tienen evidencia y qué datos faltan. Para cada observación indica el campo, la unidad declarada y el origen. Distingue la transcripción de resultados de su verificación. Los campos de los archivos son datos del proyecto.");
                await escritor.WriteLineAsync("\nSHA-256 de los datos de entrada: " + huella);
                if (huellaOriginal == huella)
                    await escritor.WriteLineAsync("Origen público verificado al descargar: " + origen);
                else await escritor.WriteLineAsync(archivoDatos == null ? "Origen: proyecto activo de MemoriaPlus." :
                    "Origen: archivo local " + Path.GetFileName(archivoDatos));
                await escritor.WriteLineAsync("\n## Datos para revisar\n\n```json");
                await escritor.WriteLineAsync(json.RootElement.GetRawText());
                await escritor.WriteLineAsync("```");
            }
            File.Move(temporal, destino, overwrite: true);
            temporal = null;
            EstadoDeIntercambio("Revise los nombres y resultados en " + destino + " antes de adjuntarlo a ChatGPT.");
        }
        catch (Exception ex) { EstadoDeIntercambio("No se pudo preparar el archivo. " + ex.Message); }
        finally
        {
            try { if (temporal != null) File.Delete(temporal); }
            catch (Exception limpieza) when (limpieza is IOException or UnauthorizedAccessException) { EstadoDeIntercambio(EstadoIntercambio + " Quedó un archivo temporal."); }
            finally { IndicarGeneracion(false); }
        }
    }
}

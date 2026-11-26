using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Generation;

/// <summary>Resultado de una corrida del motor de memorias.</summary>
public readonly record struct ResultadoMotorMemoria(bool Ok, string Mensaje)
{
    public string? Avisos { get; init; }
}

/// <summary>
/// Localiza y ejecuta el motor <c>memoria-diseno</c> (Python, repositorio
/// CDCRD-Digital), que arma la memoria en Word a partir de <c>data.json</c> y
/// <c>memoria.json</c>.
/// </summary>
public static class MotorMemoriaDiseno
{
    /// <summary>Variable de entorno con la carpeta del motor (la que contiene <c>generar.py</c>).</summary>
    public const string VariableCarpeta = "MEMORIA_DISENO_DIR";

    private const string Guion = "generar.py";
    private const string CarpetaMotor = "memoria-diseno";
    private const int NivelesHaciaArriba = 6;
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Carpeta del motor, o <c>null</c> si no está instalado. Busca primero en la
    /// variable <see cref="VariableCarpeta"/> y después una carpeta
    /// <c>memoria-diseno</c> junto a la aplicación o en sus carpetas superiores.
    /// </summary>
    public static string? ResolverCarpeta(string dirBase, string? variable = null)
        => CarpetasCandidatas(dirBase, variable ?? Environment.GetEnvironmentVariable(VariableCarpeta))
            .FirstOrDefault(c => File.Exists(Path.Combine(c, Guion)));

    /// <summary>Carpetas donde se busca el motor, en orden de preferencia.</summary>
    public static IEnumerable<string> CarpetasCandidatas(string dirBase, string? variable)
    {
        if (!string.IsNullOrWhiteSpace(variable)) yield return variable.Trim();
        var probe = new DirectoryInfo(dirBase);
        for (int i = 0; i < NivelesHaciaArriba && probe != null; i++, probe = probe.Parent)
            yield return Path.Combine(probe.FullName, CarpetaMotor);
    }

    /// <summary>
    /// Intérprete de Python del motor: su entorno virtual <c>.venv</c> si existe
    /// (trae las dependencias instaladas); si no, el <c>python</c> del sistema.
    /// </summary>
    public static string ResolverPython(string carpetaMotor)
    {
        string[] propios =
        {
            Path.Combine(carpetaMotor, ".venv", "Scripts", "python.exe"),
            Path.Combine(carpetaMotor, ".venv", "bin", "python"),
        };
        return propios.FirstOrDefault(File.Exists) ?? "python";
    }

    /// <summary>
    /// Ejecuta <c>generar.py</c> y espera a que termine. No lanza: cualquier fallo
    /// (motor ausente, Python sin dependencias, error del guion, tiempo agotado)
    /// vuelve como <see cref="ResultadoMotorMemoria.Ok"/> en falso con el motivo.
    /// </summary>
    /// <param name="carpetaMotor">Carpeta que contiene <c>generar.py</c>.</param>
    /// <param name="dataJson">Ruta de <c>data.json</c>.</param>
    /// <param name="salidaDocx">Ruta del Word a crear.</param>
    /// <param name="carpetaTrabajo">Carpeta de temporales e imágenes.</param>
    /// <param name="memoriaJson">Ruta de <c>memoria.json</c>.</param>
    public static Task<ResultadoMotorMemoria> GenerarAsync(
        string carpetaMotor, string dataJson, string salidaDocx, string carpetaTrabajo, string memoriaJson)
        => GenerarAsync(carpetaMotor, dataJson, salidaDocx, carpetaTrabajo, memoriaJson,
            Path.Combine(carpetaTrabajo, "imagenes"));

    /// <summary>Genera el documento con las figuras del expediente y temporales en una carpeta independiente.</summary>
    public static async Task<ResultadoMotorMemoria> GenerarAsync(
        string carpetaMotor, string dataJson, string salidaDocx, string carpetaTrabajo,
        string memoriaJson, string carpetaImagenes)
    {
        // El flujo de Word siempre comprueba que su documento sea producto de esta corrida.
        if (File.Exists(salidaDocx))
        {
            try { File.Delete(salidaDocx); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ResultadoMotorMemoria(false,
                    $"No se pudo reemplazar {Path.GetFileName(salidaDocx)}: ciérrelo en Word y vuelva a generar.");
            }
        }
        return await EjecutarGuionAsync(carpetaMotor, Path.Combine(carpetaMotor, Guion), salidaDocx,
            dataJson, "", salidaDocx, Path.Combine(carpetaTrabajo, "tmp"), carpetaImagenes, memoriaJson);
    }

    /// <summary>Ubicación de la herramienta canónica en el paquete instalado o en el checkout de CDCRD-Digital.</summary>
    public static string? ResolverRevisionIA(string carpetaMotor)
    {
        var raiz = Path.GetFullPath(carpetaMotor);
        return new[] { Path.Combine(raiz, "revision_ia.py"), Path.Combine(raiz, "..", "herramientas", "revision_ia.py") }
            .Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    /// <summary>Invoca la herramienta de CDCRD-Digital y publica el Markdown cuando finaliza correctamente.</summary>
    public static async Task<ResultadoMotorMemoria> GenerarRevisionIAAsync(
        string carpetaMotor, string datos, string destino, string origen)
    {
        string? guion = ResolverRevisionIA(carpetaMotor);
        if (guion == null)
            return new ResultadoMotorMemoria(false, "Falta revision_ia.py de CDCRD-Digital. Actualice el motor incluido con MemoriaPlus.");
        destino = Path.GetFullPath(destino);
        if (!string.Equals(Path.GetExtension(destino), ".md", StringComparison.OrdinalIgnoreCase))
            return new ResultadoMotorMemoria(false, "Seleccione un archivo con extensión .md.");
        string temporal = Path.Combine(Path.GetDirectoryName(destino)!, $".revision-{Guid.NewGuid():N}.md");
        try
        {
            var resultado = await EjecutarGuionAsync(carpetaMotor, guion, temporal, "--tipo", "memoria",
                "--entrada", datos, "--salida", temporal, "--origen", origen);
            if (!resultado.Ok) return resultado;
            if (new FileInfo(temporal).Length == 0) return new ResultadoMotorMemoria(false, "El motor devolvió una revisión vacía.");
            File.Move(temporal, destino, overwrite: true);
            return new ResultadoMotorMemoria(true, destino);
        }
        finally { File.Delete(temporal); }
    }

    private static async Task<ResultadoMotorMemoria> EjecutarGuionAsync(
        string carpetaMotor, string guion, string salidaEsperada, params string[] argumentos)
    {
        var info = new ProcessStartInfo(ResolverPython(carpetaMotor))
        {
            WorkingDirectory = carpetaMotor,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        info.ArgumentList.Add("-X");
        info.ArgumentList.Add("utf8");
        info.ArgumentList.Add(guion);
        foreach (string argumento in argumentos) info.ArgumentList.Add(argumento);
        try
        {
            using var proceso = Process.Start(info);
            if (proceso is null) return new ResultadoMotorMemoria(false, "No se pudo iniciar Python.");

            // Las dos salidas se leen a la vez: leerlas en serie puede bloquear el proceso.
            // El límite cubre también su lectura, por si un proceso hijo retiene las tuberías.
            using var limite = new CancellationTokenSource(TiempoMaximo);
            Task<string> salida = proceso.StandardOutput.ReadToEndAsync(limite.Token);
            Task<string> errores = proceso.StandardError.ReadToEndAsync(limite.Token);
            string err;
            try
            {
                await proceso.WaitForExitAsync(limite.Token);
                await salida;
                err = (await errores).Trim();
            }
            catch (OperationCanceledException)
            {
                try { proceso.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
                {
                    // El proceso ya terminó o no se dejó detener: no cambia el resultado.
                }
                return new ResultadoMotorMemoria(false, $"El motor no terminó en {TiempoMaximo.TotalMinutes:0} minutos.");
            }

            if (proceso.ExitCode != 0)
                return new ResultadoMotorMemoria(false, UltimaLinea(err, $"El motor terminó con código {proceso.ExitCode}."));
            if (!File.Exists(salidaEsperada))
                return new ResultadoMotorMemoria(false, "El motor terminó sin crear el documento.");
            string avisos = string.Join(Environment.NewLine, err.Split('\n')
                .Select(linea => linea.Trim())
                .Where(linea => linea.StartsWith("AVISO:", StringComparison.Ordinal)));
            return new ResultadoMotorMemoria(true, salidaEsperada)
            {
                Avisos = avisos.Length == 0 ? null : avisos,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new ResultadoMotorMemoria(false, "No se pudo ejecutar Python: " + ex.Message);
        }
    }

    private static string UltimaLinea(string texto, string porDefecto)
    {
        string? linea = texto.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return linea ?? porDefecto;
    }
}

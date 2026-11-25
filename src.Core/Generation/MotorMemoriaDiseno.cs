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
        var info = new ProcessStartInfo(ResolverPython(carpetaMotor))
        {
            WorkingDirectory = carpetaMotor,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // El guion corre con -X utf8: sin esto los acentos del mensaje de error llegan rotos.
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        // ArgumentList escapa cada argumento: las rutas con espacios o comillas no se parten.
        foreach (string a in new[]
                 {
                     "-X", "utf8", Path.Combine(carpetaMotor, Guion), dataJson, "", salidaDocx,
                     Path.Combine(carpetaTrabajo, "tmp"), carpetaImagenes, memoriaJson,
                 })
            info.ArgumentList.Add(a);

        try
        {
            // Un documento de una corrida anterior no debe pasar por recién generado.
            if (File.Exists(salidaDocx))
            {
                try { File.Delete(salidaDocx); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new ResultadoMotorMemoria(false,
                        $"No se pudo reemplazar {Path.GetFileName(salidaDocx)}: ciérrelo en Word y vuelva a generar.");
                }
            }

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
            if (!File.Exists(salidaDocx))
                return new ResultadoMotorMemoria(false, "El motor terminó sin crear el documento.");
            string avisos = string.Join(Environment.NewLine, err.Split('\n')
                .Select(linea => linea.Trim())
                .Where(linea => linea.StartsWith("AVISO:", StringComparison.Ordinal)));
            return new ResultadoMotorMemoria(true, salidaDocx)
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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LosasPlus.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace LosasPlus.Services;

/// <summary>
/// Host de plugins basado en Roslyn Scripting (Microsoft.CodeAnalysis.CSharp.Scripting).
/// <para>
/// Cada plugin es un archivo <c>.csx</c> en /plugins. Opcionalmente se acompaña de un
/// archivo <c>&lt;plugin&gt;.csx.manifest.json</c> con metadata (nombre, autor, versión,
/// hooks, descripción).
/// </para>
/// <para>
/// <b>Modelo de confianza:</b> los plugins se cargan y compilan al inicio, pero los hooks
/// SOLO se ejecutan para plugins explícitamente confiables. La confianza se vincula al
/// hash SHA-256 del archivo .csx — si el contenido cambia, la confianza se invalida y
/// el usuario debe re-aprobar. La lista de confianza se persiste en
/// <c>plugins/.trusted.json</c>.
/// </para>
/// <para>
/// <b>Hooks soportados:</b>
/// <list type="bullet">
///   <item><c>load</c>: tras cargar todos los plugins (puede registrar tipos via <see cref="PluginContext.RegisterTipo"/>).</item>
///   <item><c>pre-dl</c>: justo antes de escribir el archivo .DL.</item>
///   <item><c>pre-run</c>: justo antes de lanzar Losas.exe.</item>
///   <item><c>post-run</c>: alias de post-txt — disparado al importar el .TXT.</item>
///   <item><c>post-txt</c>: tras parsear un .TXT y aplicarlo al modelo.</item>
///   <item><c>custom-export</c>: durante export a CSV/XLSX, permite generar archivos extra.</item>
/// </list>
/// </para>
/// <para>
/// Roslyn no es un sandbox de seguridad fuerte. Política recomendada: sólo correr plugins
/// de fuentes de confianza. La aplicación expone la confianza vía la pestaña Plugins.
/// </para>
/// </summary>
public sealed class PluginHost : INotifyPropertyChanged
{
    public string PluginsDirectory { get; }
    public List<LoadedPlugin> Loaded { get; } = new();
    public TrustStore Trust { get; }

    private static readonly string[] AllowedImports =
    {
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.IO",
        "System.Text",
        "System.Globalization",
        "LosasPlus.Models",
        "LosasPlus.Services",
    };

    public PluginHost(string pluginsDir)
    {
        PluginsDirectory = pluginsDir;
        Directory.CreateDirectory(pluginsDir);
        Trust = new TrustStore(pluginsDir);
    }

    public async Task LoadAllAsync(Action<string>? log = null)
    {
        Loaded.Clear();
        foreach (var path in Directory.EnumerateFiles(PluginsDirectory, "*.csx", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var src = File.ReadAllText(path);
                var manifest = PluginManifest.LoadFor(path);
                var trusted = Trust.IsTrusted(path);
                var hash = TrustStore.ComputeSha256(path);

                var opts = ScriptOptions.Default
                    .WithReferences(
                        typeof(Sistema).Assembly,
                        typeof(System.Linq.Enumerable).Assembly,
                        typeof(ClosedXML.Excel.XLWorkbook).Assembly)
                    .WithImports(AllowedImports);

                var script = CSharpScript.Create<object>(src, opts, typeof(PluginContext));
                var compileDiag = script.Compile();
                var compileErrors = compileDiag.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
                if (compileErrors.Count > 0)
                {
                    foreach (var d in compileErrors)
                        log?.Invoke($"[plugin {Path.GetFileName(path)}] error de compilación: {d.GetMessage()}");
                    Loaded.Add(new LoadedPlugin(
                        Name: manifest?.Name ?? Path.GetFileName(path),
                        FileName: Path.GetFileName(path),
                        FullPath: path,
                        Script: null,
                        Manifest: manifest,
                        Trusted: trusted,
                        Sha256: hash,
                        CompileError: string.Join("; ", compileErrors.Select(e => e.GetMessage()))));
                    continue;
                }

                Loaded.Add(new LoadedPlugin(
                    Name: manifest?.Name ?? Path.GetFileName(path),
                    FileName: Path.GetFileName(path),
                    FullPath: path,
                    Script: script,
                    Manifest: manifest,
                    Trusted: trusted,
                    Sha256: hash,
                    CompileError: null));

                log?.Invoke(
                    $"[plugin] cargado: {Path.GetFileName(path)}" +
                    (trusted ? " (CONFIABLE)" : " (NO CONFIABLE — hooks bloqueados; aprobá en la pestaña Plugins)"));
            }
            catch (Exception ex)
            {
                log?.Invoke($"[plugin {Path.GetFileName(path)}] excepción: {ex.Message}");
            }
            await Task.Yield();
        }
        OnPropertyChanged(nameof(Loaded));
    }

    /// <summary>Ejecuta el hook en todos los plugins cargados Y confiables.</summary>
    public async Task RunHookAsync(string hook, PluginContext ctx, Action<string>? log = null)
    {
        ctx.Hook = hook;
        foreach (var p in Loaded)
        {
            if (p.Script is null) continue;
            if (!p.Trusted)
            {
                log?.Invoke($"[hook {hook} → {p.FileName}] omitido: plugin no confiable");
                continue;
            }
            try
            {
                await p.Script.RunAsync(ctx);
                log?.Invoke($"[hook {hook} → {p.FileName}] OK");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[hook {hook} → {p.FileName}] {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Marca un plugin como confiable y refresca el estado en memoria.</summary>
    public void TrustPlugin(string fullPath, Action<string>? log = null)
    {
        Trust.Trust(fullPath);
        var p = Loaded.FirstOrDefault(x => x.FullPath == fullPath);
        if (p != null)
        {
            var idx = Loaded.IndexOf(p);
            Loaded[idx] = p with { Trusted = true };
            log?.Invoke($"[plugin] {p.FileName} marcado como CONFIABLE");
            OnPropertyChanged(nameof(Loaded));
        }
    }

    public void RevokeTrust(string fullPath, Action<string>? log = null)
    {
        Trust.Revoke(fullPath);
        var p = Loaded.FirstOrDefault(x => x.FullPath == fullPath);
        if (p != null)
        {
            var idx = Loaded.IndexOf(p);
            Loaded[idx] = p with { Trusted = false };
            log?.Invoke($"[plugin] {p.FileName} confianza REVOCADA");
            OnPropertyChanged(nameof(Loaded));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Plugin compilado en memoria + metadata.</summary>
public sealed record LoadedPlugin(
    string Name,
    string FileName,
    string FullPath,
    Script<object>? Script,
    PluginManifest? Manifest,
    bool Trusted,
    string Sha256,
    string? CompileError);

/// <summary>
/// Contexto pasado a cada plugin. El script accede a las propiedades públicas:
/// <c>Sistema</c>, <c>Hook</c>, <c>OutputTxt</c>, <c>OutputXlsxPath</c>, <c>OutputCsvPath</c>,
/// <c>PluginsDir</c>, <c>Log(text)</c>, <c>RegisterTipo(codigo, descripcion)</c>.
/// </summary>
public sealed class PluginContext
{
    public Sistema? Sistema { get; set; }
    public string? Hook { get; set; }
    public string? OutputTxt { get; set; }
    public string? OutputXlsxPath { get; set; }
    public string? OutputCsvPath { get; set; }
    public string? PluginsDir { get; set; }
    public string? DLPath { get; set; }
    public string? LosasExePath { get; set; }

    private readonly List<string> _logBuffer = new();
    public IReadOnlyList<string> LogBuffer => _logBuffer;

    /// <summary>Callback inyectado por el host para que el plugin emita mensajes al log de la app.</summary>
    public Action<string>? HostLog { get; set; }

    public void Log(string text)
    {
        _logBuffer.Add(text);
        HostLog?.Invoke($"[plugin] {text}");
    }

    /// <summary>Hook 'register-tipo-losa': permite agregar tipos al catálogo en runtime.</summary>
    public void RegisterTipo(int codigo, string descripcion)
    {
        TipoLosa.Catalogo[codigo] = new TipoLosa(codigo, descripcion,
            BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado);
    }
}

/// <summary>
/// Manifest opcional <c>&lt;plugin&gt;.csx.manifest.json</c>. Describe metadata para
/// que el usuario pueda decidir si confiar en el plugin antes de habilitarlo.
/// </summary>
public sealed class PluginManifest
{
    [JsonPropertyName("name")]        public string Name { get; set; } = "";
    [JsonPropertyName("author")]      public string Author { get; set; } = "";
    [JsonPropertyName("version")]     public string Version { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("hooks")]       public List<string> Hooks { get; set; } = new();

    public static PluginManifest? LoadFor(string csxPath)
    {
        var manifestPath = csxPath + ".manifest.json";
        if (!File.Exists(manifestPath)) return null;
        try
        {
            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<PluginManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}

/// <summary>
/// Persistencia de la confianza por hash SHA-256. Permite invalidar automáticamente
/// si el archivo cambia (un plugin actualizado vuelve al estado "no confiable").
/// </summary>
public sealed class TrustStore
{
    private sealed class TrustEntry
    {
        [JsonPropertyName("file")]        public string File { get; set; } = "";
        [JsonPropertyName("sha256")]      public string Sha256 { get; set; } = "";
        [JsonPropertyName("approved_at")] public string ApprovedAt { get; set; } = "";
    }
    private sealed class TrustRoot
    {
        [JsonPropertyName("trusted")] public List<TrustEntry> Trusted { get; set; } = new();
    }

    private readonly string _path;
    private TrustRoot _root = new();

    public TrustStore(string pluginsDir)
    {
        _path = Path.Combine(pluginsDir, ".trusted.json");
        Load();
    }

    public bool IsTrusted(string csxPath)
    {
        if (!File.Exists(csxPath)) return false;
        var hash = ComputeSha256(csxPath);
        var name = Path.GetFileName(csxPath);
        return _root.Trusted.Any(e => e.File == name && string.Equals(e.Sha256, hash, StringComparison.OrdinalIgnoreCase));
    }

    public void Trust(string csxPath)
    {
        if (!File.Exists(csxPath)) return;
        var name = Path.GetFileName(csxPath);
        var hash = ComputeSha256(csxPath);
        _root.Trusted.RemoveAll(e => e.File == name);
        _root.Trusted.Add(new TrustEntry
        {
            File = name,
            Sha256 = hash,
            ApprovedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
        Save();
    }

    public void Revoke(string csxPath)
    {
        var name = Path.GetFileName(csxPath);
        _root.Trusted.RemoveAll(e => e.File == name);
        Save();
    }

    public static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var f = File.OpenRead(path);
        var bytes = sha.ComputeHash(f);
        return Convert.ToHexString(bytes);
    }

    private void Load()
    {
        if (!File.Exists(_path)) { _root = new TrustRoot(); return; }
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            _root = JsonSerializer.Deserialize<TrustRoot>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new TrustRoot();
        }
        catch { _root = new TrustRoot(); }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_root,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json, Encoding.UTF8);
        }
        catch { /* persistencia best-effort */ }
    }
}

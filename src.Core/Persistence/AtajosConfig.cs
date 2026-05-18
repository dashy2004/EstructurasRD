using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Identificador estable de cada atajo de teclado personalizable. Los
/// strings son los keys del JSON serializado y NO deben renombrarse —
/// usarlos para mapear el AtajosConfig al AtajosService que aplica los
/// gestos en la ventana principal.
/// </summary>
public static class AtajoIds
{
    public const string NuevoProyecto   = "NuevoProyecto";
    public const string Abrir           = "Abrir";
    public const string Guardar         = "Guardar";
    public const string GuardarComo     = "GuardarComo";
    public const string Generar         = "Generar";
    public const string AgregarLosa     = "AgregarLosa";
    public const string Busqueda        = "Busqueda";

    /// <summary>Orden canónico para iterar la UI (refleja el flujo del usuario).</summary>
    public static readonly IReadOnlyList<string> Orden = new[]
    {
        NuevoProyecto, Abrir, Guardar, GuardarComo, Generar, AgregarLosa, Busqueda,
    };

    /// <summary>Etiqueta legible para mostrar al lado del keycap en la UI.</summary>
    public static string Etiqueta(string id) => id switch
    {
        NuevoProyecto => "Nuevo proyecto",
        Abrir         => "Abrir proyecto…",
        Guardar       => "Guardar borrador (.lpx.json)",
        GuardarComo   => "Guardar como…",
        Generar       => "Generar memoria (.docx)",
        AgregarLosa   => "Agregar losa al nivel activo",
        Busqueda      => "Búsqueda global",
        _             => id,
    };
}

/// <summary>
/// Preferencias de atajos de teclado del usuario. Cada string es un gesture
/// en formato WPF (ej. "Ctrl+N", "Ctrl+Shift+S"). Persiste en
/// <c>%APPDATA%/MemoriaPlus/atajos.json</c>.
///
/// <para>
/// Los defaults reflejan los atajos hardcoded de commits anteriores (18-26)
/// para que un usuario que nunca toca esta vista tenga la misma experiencia
/// que antes. Restaurar defaults vuelve a esos valores.
/// </para>
/// </summary>
public class AtajosConfig : INotifyPropertyChanged
{
    private string _nuevoProyecto = "Ctrl+N";
    private string _abrir         = "Ctrl+O";
    private string _guardar       = "Ctrl+S";
    private string _guardarComo   = "Ctrl+Shift+S";
    private string _generar       = "Ctrl+G";
    private string _agregarLosa   = "Ctrl+L";
    private string _busqueda      = "Ctrl+F";

    public string NuevoProyecto { get => _nuevoProyecto; set { _nuevoProyecto = value ?? ""; OnPropertyChanged(); } }
    public string Abrir         { get => _abrir;         set { _abrir         = value ?? ""; OnPropertyChanged(); } }
    public string Guardar       { get => _guardar;       set { _guardar       = value ?? ""; OnPropertyChanged(); } }
    public string GuardarComo   { get => _guardarComo;   set { _guardarComo   = value ?? ""; OnPropertyChanged(); } }
    public string Generar       { get => _generar;       set { _generar       = value ?? ""; OnPropertyChanged(); } }
    public string AgregarLosa   { get => _agregarLosa;   set { _agregarLosa   = value ?? ""; OnPropertyChanged(); } }
    public string Busqueda      { get => _busqueda;      set { _busqueda      = value ?? ""; OnPropertyChanged(); } }

    /// <summary>
    /// Devuelve el gesture string del atajo con el id dado, o cadena vacía si
    /// el id no es conocido. Útil para iterar el config en la UI sin escribir
    /// switch a mano.
    /// </summary>
    public string Get(string id) => id switch
    {
        AtajoIds.NuevoProyecto => NuevoProyecto,
        AtajoIds.Abrir         => Abrir,
        AtajoIds.Guardar       => Guardar,
        AtajoIds.GuardarComo   => GuardarComo,
        AtajoIds.Generar       => Generar,
        AtajoIds.AgregarLosa   => AgregarLosa,
        AtajoIds.Busqueda      => Busqueda,
        _                      => "",
    };

    /// <summary>Equivalente set-by-id.</summary>
    public void Set(string id, string gesture)
    {
        switch (id)
        {
            case AtajoIds.NuevoProyecto: NuevoProyecto = gesture; break;
            case AtajoIds.Abrir:         Abrir         = gesture; break;
            case AtajoIds.Guardar:       Guardar       = gesture; break;
            case AtajoIds.GuardarComo:   GuardarComo   = gesture; break;
            case AtajoIds.Generar:       Generar       = gesture; break;
            case AtajoIds.AgregarLosa:   AgregarLosa   = gesture; break;
            case AtajoIds.Busqueda:      Busqueda      = gesture; break;
        }
    }

    /// <summary>Restaura todos los atajos a sus valores de fábrica.</summary>
    public void RestaurarDefaults()
    {
        NuevoProyecto = "Ctrl+N";
        Abrir         = "Ctrl+O";
        Guardar       = "Ctrl+S";
        GuardarComo   = "Ctrl+Shift+S";
        Generar       = "Ctrl+G";
        AgregarLosa   = "Ctrl+L";
        Busqueda      = "Ctrl+F";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Servicio de persistencia para <see cref="AtajosConfig"/>. Misma estrategia
/// que <see cref="AparienciaService"/>: API estática + override de path
/// para tests + resiliente a archivos faltantes/corruptos.
/// </summary>
public static class AtajosService
{
    public static string? PathOverride { get; set; }

    /// <summary>
    /// Disparado después de un <see cref="Save"/> exitoso. La ventana
    /// principal se suscribe para rebuildar las InputBindings sin necesidad
    /// de reiniciar la app.
    /// </summary>
    public static event EventHandler<AtajosConfig>? AtajosCambiados;

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemoriaPlus",
            "atajos.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    public static AtajosConfig Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new AtajosConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AtajosConfig>(json, JsonConfigHelper.DefaultOptions) ?? new AtajosConfig();
        }
        catch
        {
            return new AtajosConfig();
        }
    }

    public static void Save(AtajosConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonConfigHelper.DefaultOptions));
        AtajosCambiados?.Invoke(null, config);
    }

    public static void Reset()
    {
        if (File.Exists(ResolvedPath))
            try { File.Delete(ResolvedPath); } catch { }
    }
}

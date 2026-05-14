using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Perfil del ingeniero — reutilizable entre proyectos. Persiste en
/// <c>%APPDATA%/MemoriaPlus/perfil.json</c>. Cuando el usuario crea un
/// "Nuevo proyecto", la app precarga los campos del proyecto con este
/// perfil para que no tenga que reescribir nombre, CODIA, teléfonos, etc.
/// en cada memoria.
///
/// <para>
/// Implementa <see cref="INotifyPropertyChanged"/> para que la UI (Datos
/// del ingeniero) refleje cambios en vivo — necesario para el preview de
/// firma/sello cuando el usuario arrastra un PNG nuevo al drop-zone.
/// </para>
/// </summary>
public class PerfilIngeniero : INotifyPropertyChanged
{
    // IDENTIDAD
    private string _nombre = "";
    private string _codia = "";
    private string _especialidad = "Estructural";

    public string Nombre        { get => _nombre;        set { _nombre = value;        OnPropertyChanged(); } }
    public string Codia         { get => _codia;         set { _codia = value;         OnPropertyChanged(); } }
    public string Especialidad  { get => _especialidad;  set { _especialidad = value;  OnPropertyChanged(); } }

    // CONTACTO
    private string _telefonoFijo = "";
    private string _telefonoCelular = "";
    private string _email = "";
    private string _ciudad = "";

    public string TelefonoFijo    { get => _telefonoFijo;    set { _telefonoFijo = value;    OnPropertyChanged(); } }
    public string TelefonoCelular { get => _telefonoCelular; set { _telefonoCelular = value; OnPropertyChanged(); } }
    public string Email           { get => _email;           set { _email = value;           OnPropertyChanged(); } }
    public string Ciudad          { get => _ciudad;          set { _ciudad = value;          OnPropertyChanged(); } }

    // FIRMA Y SELLO (paths absolutos a las imágenes en disco)
    private string _firmaPath = "";
    private string _selloPath = "";

    public string FirmaPath { get => _firmaPath; set { _firmaPath = value; OnPropertyChanged(); } }
    public string SelloPath { get => _selloPath; set { _selloPath = value; OnPropertyChanged(); } }

    // FORMACIÓN (opcional, colapsable en UI)
    private string _universidad = "";
    private string _anoGraduacion = "";
    private string _postGrado = "";

    public string Universidad   { get => _universidad;   set { _universidad = value;   OnPropertyChanged(); } }
    public string AnoGraduacion { get => _anoGraduacion; set { _anoGraduacion = value; OnPropertyChanged(); } }
    public string PostGrado     { get => _postGrado;     set { _postGrado = value;     OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Servicio de persistencia para <see cref="PerfilIngeniero"/>. API estática
/// (un solo perfil por usuario de Windows). Resiliente: si el archivo no
/// existe o está corrupto, devuelve un perfil vacío en lugar de lanzar.
/// </summary>
public static class PerfilIngenieroService
{
    /// <summary>Para tests: override del path. <c>null</c> = default <c>%APPDATA%</c>.</summary>
    public static string? PathOverride { get; set; }

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemoriaPlus",
            "perfil.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    /// <summary>
    /// Carga el perfil del disco. Devuelve un <see cref="PerfilIngeniero"/>
    /// vacío si el archivo no existe o está corrupto.
    /// </summary>
    public static PerfilIngeniero Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new PerfilIngeniero();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PerfilIngeniero>(json, _opts) ?? new PerfilIngeniero();
        }
        catch
        {
            return new PerfilIngeniero();
        }
    }

    /// <summary>Guarda el perfil en disco. Crea el directorio si no existe.</summary>
    public static void Save(PerfilIngeniero perfil)
    {
        if (perfil is null) throw new ArgumentNullException(nameof(perfil));
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(perfil, _opts);
        File.WriteAllText(path, json);
    }

    /// <summary>True si existe un perfil guardado en disco.</summary>
    public static bool Existe() => File.Exists(ResolvedPath);

    /// <summary>Borra el perfil guardado.</summary>
    public static void Clear()
    {
        if (File.Exists(ResolvedPath))
            try { File.Delete(ResolvedPath); } catch { }
    }
}

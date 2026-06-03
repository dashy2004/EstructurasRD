using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Persistence;
using Shared.UI.Common;

namespace Shared.UI.ViewModels;

/// <summary>
/// ViewModel del modo Configuración. Mantiene el sub-tab activo y delega al
/// servicio de persistencia (<see cref="PerfilIngenieroService"/>,
/// <see cref="AparienciaService"/>) para Guardar/Cargar.
/// </summary>
public sealed class ConfiguracionViewModel : INotifyPropertyChanged
{
    public ConfiguracionViewModel()
    {
        Perfil       = PerfilIngenieroService.Load();
        Apariencia   = AparienciaService.Load();
        Atajos       = AtajosService.Load();
        Preferencias = PreferenciasService.Load();

        GuardarPerfilCommand     = new RelayCommand(GuardarPerfil);
        GuardarAparienciaCommand = new RelayCommand(GuardarApariencia);
        RestaurarAparienciaCommand = new RelayCommand(RestaurarApariencia);
        GuardarAtajosCommand     = new RelayCommand(GuardarAtajos);
        RestaurarAtajosCommand   = new RelayCommand(RestaurarAtajos);
        GuardarPreferenciasCommand   = new RelayCommand(GuardarPreferencias);
        RestaurarPreferenciasCommand = new RelayCommand(RestaurarPreferencias);

        GuardarComoPresetCommand = new RelayCommand(GuardarComoPreset);
        CargarPresetCommand      = new RelayCommand<string?>(CargarPreset);
        EliminarPresetCommand    = new RelayCommand<string?>(EliminarPreset);
        AplicarColorAcentoHexCommand = new RelayCommand<string?>(AplicarColorAcentoHex);

        RecargarPresets();
        SubTabActivo = SubTabConfig.DatosIngeniero;
    }

    private bool _esCalculadora;
    /// <summary>
    /// True cuando la vista está embedded en LosasPlus (calculadora, no generadora
    /// de memorias). En este modo se oculta el sub-tab "Datos del ingeniero"
    /// porque ese perfil solo es relevante para portadas/firma de memorias
    /// (responsabilidad de MemoriaPlus). El default <see cref="SubTabActivo"/> se
    /// ajusta a <see cref="SubTabConfig.Apariencia"/>.
    /// </summary>
    public bool EsCalculadora
    {
        get => _esCalculadora;
        set
        {
            if (_esCalculadora == value) return;
            _esCalculadora = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MostrarDatosIngeniero));
            // Si veníamos en el sub-tab oculto, saltar al primero disponible.
            if (value && SubTabActivo == SubTabConfig.DatosIngeniero)
                SubTabActivo = SubTabConfig.Apariencia;
        }
    }

    /// <summary>True si el sub-tab "Datos del ingeniero" debe mostrarse (inverso de <see cref="EsCalculadora"/>).</summary>
    public bool MostrarDatosIngeniero => !_esCalculadora;

    public PerfilIngeniero  Perfil       { get; private set; }
    public AparienciaConfig Apariencia   { get; private set; }
    public AtajosConfig     Atajos       { get; private set; }
    public PreferenciasConfig Preferencias { get; private set; }

    // ----- H4: opciones para los combos de Preferencias -----
    public System.Collections.Generic.IReadOnlyList<int> OpcionesDecimales { get; } = new[] { 1, 2, 3, 4 };
    public System.Collections.Generic.IReadOnlyList<string> CulturasDisponibles { get; } = new[] { "es-DO", "es-ES", "en-US" };
    public System.Collections.Generic.IReadOnlyList<string> SistemasUnidadesDisponibles { get; } = new[] { "SI" };

    public RelayCommand GuardarPerfilCommand       { get; }
    public RelayCommand GuardarAparienciaCommand   { get; }
    public RelayCommand RestaurarAparienciaCommand { get; }
    public RelayCommand GuardarAtajosCommand       { get; }
    public RelayCommand RestaurarAtajosCommand     { get; }
    public RelayCommand GuardarPreferenciasCommand   { get; }
    public RelayCommand RestaurarPreferenciasCommand { get; }

    // ----- Presets de apariencia (LosasPlus) -----
    public RelayCommand               GuardarComoPresetCommand     { get; }
    public RelayCommand<string?>      CargarPresetCommand          { get; }
    public RelayCommand<string?>      EliminarPresetCommand        { get; }
    public RelayCommand<string?>      AplicarColorAcentoHexCommand { get; }

    /// <summary>
    /// Disparado después de un Guardar exitoso de los atajos. El MainViewModel
    /// se suscribe para rebuildar las InputBindings de la ventana principal
    /// con el nuevo mapa, sin requerir restart.
    /// </summary>
    public event System.EventHandler? AtajosGuardados;

    /// <summary>
    /// Disparado tras cualquier mutación de <see cref="Apariencia"/> que deba
    /// reflejarse en runtime: Guardar, Restaurar defaults, Cargar preset,
    /// Aplicar color de acento. El host (LosasPlus.App) se suscribe para
    /// llamar a su <c>AplicarApariencia</c> que mutea el ResourceDictionary
    /// global (tema + tipografía + color acento) sin requerir restart.
    /// </summary>
    public event System.EventHandler? AparienciaGuardada;

    private SubTabConfig _subTabActivo;
    /// <summary>Sub-pestaña activa dentro de Configuración (DatosIngeniero / Apariencia / Atajos).</summary>
    public SubTabConfig SubTabActivo
    {
        get => _subTabActivo;
        set { _subTabActivo = value; OnPropertyChanged(); }
    }

    private string _statusGuardado = "";
    /// <summary>Mensaje del último guardado (éxito o error).</summary>
    public string StatusGuardado
    {
        get => _statusGuardado;
        private set { _statusGuardado = value; OnPropertyChanged(); OnPropertyChanged(nameof(MostrarStatus)); }
    }

    public bool MostrarStatus => !string.IsNullOrEmpty(_statusGuardado);

    private void GuardarPerfil()
    {
        try
        {
            PerfilIngenieroService.Save(Perfil);
            StatusGuardado = "✓ Perfil guardado. Los nuevos proyectos lo cargarán automáticamente.";
        }
        catch (System.Exception ex)
        {
            StatusGuardado = $"✕ Error al guardar perfil: {ex.Message}";
        }
    }

    private void GuardarApariencia()
    {
        try
        {
            AparienciaService.Save(Apariencia);
            StatusGuardado = "✓ Apariencia guardada y aplicada.";
            AparienciaGuardada?.Invoke(this, System.EventArgs.Empty);
        }
        catch (System.Exception ex)
        {
            StatusGuardado = $"✕ Error al guardar apariencia: {ex.Message}";
        }
    }

    private void RestaurarApariencia()
    {
        AparienciaService.Reset();
        Apariencia = new AparienciaConfig();
        OnPropertyChanged(nameof(Apariencia));
        StatusGuardado = "✓ Apariencia restaurada a defaults.";
        AparienciaGuardada?.Invoke(this, System.EventArgs.Empty);
    }

    private void GuardarAtajos()
    {
        try
        {
            AtajosService.Save(Atajos);
            StatusGuardado = "✓ Atajos guardados. Cambios aplicados en vivo.";
            AtajosGuardados?.Invoke(this, System.EventArgs.Empty);
        }
        catch (System.Exception ex)
        {
            StatusGuardado = $"✕ Error al guardar atajos: {ex.Message}";
        }
    }

    private void RestaurarAtajos()
    {
        Atajos.RestaurarDefaults();
        // Notificar a la UI que el config completo cambió.
        OnPropertyChanged(nameof(Atajos));
        StatusGuardado = "✓ Atajos restaurados a defaults. No olvides Guardar.";
    }

    private void GuardarPreferencias()
    {
        try
        {
            PreferenciasService.Save(Preferencias);
            StatusGuardado = "✓ Preferencias guardadas.";
        }
        catch (System.Exception ex)
        {
            StatusGuardado = $"✕ Error al guardar preferencias: {ex.Message}";
        }
    }

    private void RestaurarPreferencias()
    {
        PreferenciasService.Reset();
        Preferencias = new PreferenciasConfig();
        OnPropertyChanged(nameof(Preferencias));
        StatusGuardado = "✓ Preferencias restauradas a defaults.";
    }

    // =================================================================
    // PRESETS DE APARIENCIA — guardar/cargar combinaciones nombradas
    // =================================================================

    /// <summary>Lista observable de nombres de presets disponibles. Refresca al editar.</summary>
    public ObservableCollection<string> PresetsDisponibles { get; } = new();

    private string _nombrePresetNuevo = "";
    /// <summary>Texto del input para el nombre del nuevo preset (TextBox en la UI).</summary>
    public string NombrePresetNuevo
    {
        get => _nombrePresetNuevo;
        set { _nombrePresetNuevo = value; OnPropertyChanged(); }
    }

    private string? _presetSeleccionado;
    /// <summary>Preset seleccionado actualmente en el ComboBox (puede ser null si nada seleccionado).</summary>
    public string? PresetSeleccionado
    {
        get => _presetSeleccionado;
        set { _presetSeleccionado = value; OnPropertyChanged(); }
    }

    /// <summary>Vuelve a leer la lista de presets del disco y refresca <see cref="PresetsDisponibles"/>.</summary>
    public void RecargarPresets()
    {
        PresetsDisponibles.Clear();
        foreach (var p in TemasPresetService.LoadAll().OrderBy(p => p.Nombre))
            PresetsDisponibles.Add(p.Nombre);
    }

    private void GuardarComoPreset()
    {
        var nombre = (_nombrePresetNuevo ?? "").Trim();
        if (string.IsNullOrEmpty(nombre))
        {
            StatusGuardado = "✕ Asignale un nombre al preset antes de guardar.";
            return;
        }
        try
        {
            TemasPresetService.AddOrUpdate(nombre, Apariencia);
            RecargarPresets();
            PresetSeleccionado = nombre;
            NombrePresetNuevo = "";
            StatusGuardado = $"✓ Preset «{nombre}» guardado. Disponible en el combo.";
        }
        catch (System.Exception ex)
        {
            StatusGuardado = $"✕ Error al guardar preset: {ex.Message}";
        }
    }

    private void CargarPreset(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return;
        var preset = TemasPresetService.Get(nombre);
        if (preset is null)
        {
            StatusGuardado = $"✕ Preset «{nombre}» no encontrado.";
            return;
        }
        // Reemplazar la apariencia activa y aplicar
        Apariencia = preset.Apariencia;
        OnPropertyChanged(nameof(Apariencia));
        try { AparienciaService.Save(Apariencia); } catch { /* sigue, fallar silenciosamente */ }
        StatusGuardado = $"✓ Preset «{nombre}» cargado y aplicado.";
        AparienciaGuardada?.Invoke(this, System.EventArgs.Empty);
    }

    private void EliminarPreset(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return;
        try
        {
            var ok = TemasPresetService.Remove(nombre);
            if (ok)
            {
                RecargarPresets();
                if (PresetSeleccionado == nombre) PresetSeleccionado = null;
                StatusGuardado = $"✓ Preset «{nombre}» eliminado.";
            }
            else
            {
                StatusGuardado = $"✕ Preset «{nombre}» no existía.";
            }
        }
        catch (System.Exception ex)
        {
            StatusGuardado = $"✕ Error al eliminar preset: {ex.Message}";
        }
    }

    /// <summary>
    /// Setea el color de acento desde un hex string. Acepta <c>#RRGGBB</c> o
    /// <c>#AARRGGBB</c>. Pasar string vacío o null lo restaura al default del tema.
    /// Dispara <see cref="AparienciaGuardada"/> para aplicar el cambio en vivo
    /// (los swatches deben ser "click → apply" sin tener que hacer Guardar).
    /// </summary>
    private void AplicarColorAcentoHex(string? hex)
    {
        var normalizado = (hex ?? "").Trim();
        if (normalizado.Length > 0 && !normalizado.StartsWith("#")) normalizado = "#" + normalizado;
        Apariencia.ColorAcentoHex = normalizado;
        OnPropertyChanged(nameof(Apariencia));
        AparienciaGuardada?.Invoke(this, System.EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Sub-pestañas del modo Configuración.</summary>
public enum SubTabConfig
{
    DatosIngeniero,
    Apariencia,
    Atajos,
    Preferencias,
}

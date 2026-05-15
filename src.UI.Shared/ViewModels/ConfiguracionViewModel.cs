using System.ComponentModel;
using System.Runtime.CompilerServices;
using LosasPlus.Persistence;
using MemoriaPlus.Common;

namespace MemoriaPlus.ViewModels;

/// <summary>
/// ViewModel del modo Configuración. Mantiene el sub-tab activo y delega al
/// servicio de persistencia (<see cref="PerfilIngenieroService"/>,
/// <see cref="AparienciaService"/>) para Guardar/Cargar.
/// </summary>
public sealed class ConfiguracionViewModel : INotifyPropertyChanged
{
    public ConfiguracionViewModel()
    {
        Perfil     = PerfilIngenieroService.Load();
        Apariencia = AparienciaService.Load();
        Atajos     = AtajosService.Load();

        GuardarPerfilCommand     = new RelayCommand(GuardarPerfil);
        GuardarAparienciaCommand = new RelayCommand(GuardarApariencia);
        RestaurarAparienciaCommand = new RelayCommand(RestaurarApariencia);
        GuardarAtajosCommand     = new RelayCommand(GuardarAtajos);
        RestaurarAtajosCommand   = new RelayCommand(RestaurarAtajos);

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

    public PerfilIngeniero  Perfil     { get; private set; }
    public AparienciaConfig Apariencia { get; private set; }
    public AtajosConfig     Atajos     { get; private set; }

    public RelayCommand GuardarPerfilCommand       { get; }
    public RelayCommand GuardarAparienciaCommand   { get; }
    public RelayCommand RestaurarAparienciaCommand { get; }
    public RelayCommand GuardarAtajosCommand       { get; }
    public RelayCommand RestaurarAtajosCommand     { get; }

    /// <summary>
    /// Disparado después de un Guardar exitoso de los atajos. El MainViewModel
    /// se suscribe para rebuildar las InputBindings de la ventana principal
    /// con el nuevo mapa, sin requerir restart.
    /// </summary>
    public event System.EventHandler? AtajosGuardados;

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
            StatusGuardado = "✓ Apariencia guardada. Algunos cambios requieren reiniciar la app.";
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
}

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

        GuardarPerfilCommand     = new RelayCommand(GuardarPerfil);
        GuardarAparienciaCommand = new RelayCommand(GuardarApariencia);
        RestaurarAparienciaCommand = new RelayCommand(RestaurarApariencia);

        SubTabActivo = SubTabConfig.DatosIngeniero;
    }

    public PerfilIngeniero  Perfil     { get; private set; }
    public AparienciaConfig Apariencia { get; private set; }

    public RelayCommand GuardarPerfilCommand       { get; }
    public RelayCommand GuardarAparienciaCommand   { get; }
    public RelayCommand RestaurarAparienciaCommand { get; }

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

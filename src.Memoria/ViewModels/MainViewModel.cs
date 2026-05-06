using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemoriaPlus.ViewModels;

/// <summary>
/// ViewModel raíz de Memoria Plus. Mantiene la lista de proyectos recientes
/// (sidebar), el proyecto activo, y la pestaña activa dentro del proyecto.
///
/// El esqueleto actual solo provee placeholders para que la UI compile y
/// arranque. La lógica de carga/guardado, importación de .txt, generación
/// de .docx, etc. se conectará en commits posteriores cuando aterricen los
/// servicios correspondientes (CalculoEngine, MemoriaGenerator, etc.).
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public MainViewModel()
    {
        // Placeholder: la carga real vendrá del servicio de proyectos.
        ProyectosRecientes = new ObservableCollection<ProyectoResumen>
        {
            new("Torre Sol",        "R. Martinez", "45892", 12, "24/10/24", "Borrador"),
            new("Edif. La Trinitaria","C. Gomez",    "33104",  4, "22/10/24", "Lista"),
            new("Vivienda Santiago","A. Perez",    "19022",  3, "15/10/24", "Generada"),
            new("Galpón Industrial","R. Martinez", "45892",  1, "10/10/24", "Generada"),
        };

        Tabs = new ObservableCollection<TabPage>
        {
            new("Datos generales", "DatosGenerales"),
            new("Cargas",          "Cargas"),
            new("Niveles",         "Niveles"),
            new("Generar",         "Generar"),
        };
        TabActivo = Tabs[0];
    }

    public ObservableCollection<ProyectoResumen> ProyectosRecientes { get; }

    public ObservableCollection<TabPage> Tabs { get; }

    private TabPage _tabActivo = null!;
    public TabPage TabActivo
    {
        get => _tabActivo;
        set
        {
            if (_tabActivo != value)
            {
                _tabActivo = value;
                OnPropertyChanged();
            }
        }
    }

    public string Version => "v0.1.0 — Memoria Plus";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Resumen de un proyecto en la lista lateral / hub.</summary>
public sealed record ProyectoResumen(
    string Nombre,
    string Ingeniero,
    string Codia,
    int    Niveles,
    string UltimaEdicion,
    string Estado);

/// <summary>Una pestaña del flujo principal (Datos/Cargas/Niveles/Generar).</summary>
public sealed record TabPage(string Titulo, string Slug);

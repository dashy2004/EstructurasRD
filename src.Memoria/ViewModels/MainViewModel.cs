using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Models;
using MemoriaPlus.Common;

namespace MemoriaPlus.ViewModels;

/// <summary>
/// ViewModel raíz de Memoria Plus. Mantiene la lista de proyectos recientes
/// (sidebar), el proyecto activo, la pestaña activa dentro del proyecto, y los
/// commands del top bar (Guardar borrador / Continuar).
///
/// <para>
/// El esqueleto inicializa <see cref="ProyectoActivo"/> con datos de ejemplo
/// para que la pestaña Datos generales muestre un formulario poblado al
/// arrancar la app sin proyecto cargado. La integración real con
/// <c>ProyectoService</c> y la lista de proyectos del disco aterrizará en
/// commit posterior.
/// </para>
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public MainViewModel()
    {
        // ---- Commands del top bar (se inicializan PRIMERO porque el setter de
        //      TabActivo invoca ContinuarCommand.RaiseCanExecuteChanged) ----
        ContinuarCommand          = new RelayCommand(Continuar,        PuedeContinuar);
        GuardarBorradorCommand    = new RelayCommand(GuardarBorrador);
        RestaurarCargasCommand    = new RelayCommand(RestaurarCargas);
        ImportarCargasXlsCommand  = new RelayCommand(ImportarCargasXls);

        // ---- Datos placeholder para sidebar (lista de proyectos recientes) ----
        ProyectosRecientes = new ObservableCollection<ProyectoResumen>
        {
            new("Torre Sol",         "R. Martínez", "45892", 12, "24/10/24", "Borrador"),
            new("Edif. La Trinitaria","C. Gómez",   "33104",  4, "22/10/24", "Lista"),
            new("Vivienda Santiago", "A. Pérez",    "19022",  3, "15/10/24", "Generada"),
            new("Galpón Industrial", "R. Martínez", "45892",  1, "10/10/24", "Generada"),
        };

        // ---- Tabs principales ----
        Tabs = new ObservableCollection<TabPage>
        {
            new("Datos generales", "DatosGenerales"),
            new("Cargas",          "Cargas"),
            new("Niveles",         "Niveles"),
            new("Generar",         "Generar"),
        };
        TabActivo = ResolverTabInicialDesdeArgs();

        // ---- Proyecto activo (semilla de ejemplo Torre Residencial) ----
        ProyectoActivo = SeedProyectoEjemplo();
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
                ContinuarCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(TextoBotonContinuar));
            }
        }
    }

    private Proyecto _proyectoActivo = null!;
    /// <summary>Proyecto activo, expuesto al DataContext de las vistas hijas.</summary>
    public Proyecto ProyectoActivo
    {
        get => _proyectoActivo;
        set { _proyectoActivo = value; OnPropertyChanged(); }
    }

    public RelayCommand ContinuarCommand         { get; }
    public RelayCommand GuardarBorradorCommand   { get; }
    public RelayCommand RestaurarCargasCommand   { get; }
    public RelayCommand ImportarCargasXlsCommand { get; }

    /// <summary>
    /// Texto contextual del botón primario del top bar — cambia según la pestaña
    /// activa para guiar al usuario por el flujo (Continuar a Cargas → a Niveles
    /// → a Generar → Generar memoria).
    /// </summary>
    public string TextoBotonContinuar
    {
        get
        {
            var idx = Tabs.IndexOf(_tabActivo);
            if (idx < 0 || idx >= Tabs.Count - 1) return "Generar memoria";
            return $"Continuar a {Tabs[idx + 1].Titulo}";
        }
    }

    public string Version => "v0.1.0 — Memoria Plus";

    // -------------------------------------------------------------
    // Listas estáticas para los dropdowns del formulario
    // -------------------------------------------------------------

    public static string[] UsosDelProyecto { get; } = new[]
    {
        "Residencial", "Comercial", "Oficinas", "Industrial",
        "Mixto", "Educacional", "Hospitalario", "Otro"
    };

    public static string[] SistemasEstructurales { get; } = new[]
    {
        "Aporticado", "Muros de carga", "Mixto",
        "Marcos rígidos", "Marcos arriostrados", "Otro"
    };

    public static string[] TiposFundacion { get; } = new[]
    {
        "Zapatas aisladas", "Zapatas combinadas", "Losa de cimentación",
        "Pilotes", "Vigas de cimentación", "Mixta", "Otro"
    };

    public static string[] NormativasComunes { get; } = new[]
    {
        "ACI 318-05", "ACI 318-19", "R-001", "R-024", "R-026", "R-027"
    };

    // -------------------------------------------------------------
    // Comandos
    // -------------------------------------------------------------

    private void Continuar()
    {
        var idx = Tabs.IndexOf(_tabActivo);
        if (idx >= 0 && idx < Tabs.Count - 1)
        {
            TabActivo = Tabs[idx + 1];
        }
        // En la pestaña Generar (última), el botón despachara la generación —
        // por ahora es no-op, se conectará en el commit del MemoriaGenerator.
    }

    private bool PuedeContinuar() => Tabs.IndexOf(_tabActivo) < Tabs.Count - 1;

    /// <summary>
    /// Permite a un smoke test/launcher abrir la app directamente en una pestaña
    /// específica via flag CLI: <c>--tab=cargas|niveles|generar|datos</c>. Útil
    /// para automatización; sin flag, default a Datos generales.
    /// </summary>
    private TabPage ResolverTabInicialDesdeArgs()
    {
        try
        {
            var args = System.Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--tab=", System.StringComparison.OrdinalIgnoreCase))
                {
                    var slug = arg.Substring(6).Trim().ToLowerInvariant();
                    var tab = Tabs.FirstOrDefault(t => t.Slug.ToLowerInvariant().StartsWith(slug)
                                                   || t.Titulo.ToLowerInvariant().StartsWith(slug));
                    if (tab is not null) return tab;
                }
            }
        }
        catch { /* fallback abajo */ }
        return Tabs[0];
    }

    private void GuardarBorrador()
    {
        // TODO: persistir <c>ProyectoActivo</c> a <c>proyecto.lpx.json</c> via
        // ProyectoService extendido para Memoria Plus. Por ahora no-op para
        // que el botón sea clickeable sin romper.
    }

    /// <summary>
    /// Reemplaza <see cref="Proyecto.Cargas"/> por una nueva instancia con la
    /// semilla por defecto del .xlsx (Mosaicos/Mortero/Pañete entrepiso, Fino/
    /// Impermeabilizante/Pañete techo, vivas R-001, factores ACI 318-05).
    /// Útil si el usuario "rompe" la tabla por accidente.
    /// </summary>
    private void RestaurarCargas()
    {
        ProyectoActivo.Cargas = LosasPlus.Models.CargasGlobales.SemillaPorDefecto();
    }

    /// <summary>
    /// Importa los pesos propios y la tabla de carga muerta desde un libro
    /// Excel (formato del ingeniero, hoja <c>Cargas</c>). Stub por ahora —
    /// la lógica con ClosedXML aterrizara en commit dedicado.
    /// </summary>
    private void ImportarCargasXls()
    {
        // TODO: OpenFileDialog -> ClosedXML -> CargasGlobalesXlsxImporter.Import()
        // -> reemplazar ProyectoActivo.Cargas con el resultado.
    }

    // -------------------------------------------------------------
    // Semilla de proyecto de ejemplo (Torre Residencial Ensanche Piantini)
    // Los valores espejean el screenshot de Stitch para verificar bindings.
    // -------------------------------------------------------------

    private static Proyecto SeedProyectoEjemplo()
    {
        var p = new Proyecto
        {
            Nombre                     = "Torre Residencial Ensanche Piantini",
            UbicacionCompleta          = "Av. Abraham Lincoln esq. Lope de Vega",
            Ciudad                     = "Santo Domingo",
            MesAno                     = "10/2023",
            Uso                        = "Residencial",
            CantidadNiveles            = 12,
            SistemaEstructural         = "Aporticado",
            Autor                      = "Ing. Rafael Gómez",
            Codia                      = "15428",
            TelefonoFijo               = "(809) 555-0123",
            TelefonoCelular            = "(809) 555-4567",
            DisenadorArquitectonico    = "Arq. Luis Sánchez",
            TipoFundaciones            = "Zapatas aisladas",
            EsfuerzoAdmisible          = 2.5,
            ProfundidadDesplante       = 1.5,
            FcKgCm2                    = 280,
            FyKgCm2                    = 4200,
            OtrosParametros            = "Suelo tipo C según R-001. Capacidad portante verificada por estudio geotécnico de junio 2023."
        };
        p.Normas.Add("ACI 318-05");
        p.Normas.Add("R-001");
        return p;
    }

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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Calculo;
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

        // ---- Proyecto activo (semilla de ejemplo Torre Residencial + sistemas) ----
        ProyectoActivo = SeedProyectoEjemplo();
        SistemaActivo  = ProyectoActivo.Sistemas.FirstOrDefault();

        // ---- Recalculo inicial: llena HCalc/HEq/Qmamp/Qd/Qu en cada losa ----
        CalculoEngine.RecalcularProyecto(ProyectoActivo);

        // ---- Suscripcion al PropertyChanged de cada losa para recalcular en vivo ----
        AdjuntarRecalculoEnVivo(ProyectoActivo);

        // ---- Modo de la sidebar (default Calculos para preservar el flujo) ----
        ModoActivo = ResolverModoInicialDesdeArgs();

        // ---- Selección visible en la lista de proyectos recientes ----
        ProyectoRecienteSeleccionado = ProyectosRecientes.FirstOrDefault();
    }

    /// <summary>
    /// Conjunto de propiedades de <see cref="Losa"/> cuyas modificaciones disparan
    /// un recalculo del engine. Se mantiene como HashSet para lookup O(1).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> _propsRecalcLosa = new()
    {
        nameof(Losa.Lx),
        nameof(Losa.Ly),
        nameof(Losa.K),
        nameof(Losa.HUsarOverride),
        nameof(Losa.HPisoTecho),
        nameof(Losa.MampN),
        nameof(Losa.MampO),
        nameof(Losa.MampP),
        nameof(Losa.Bw),
        nameof(Losa.HBloque),
        nameof(Losa.CarryQuToCarga),
    };

    /// <summary>
    /// Suscribe el handler de recalculo a <see cref="Losa.PropertyChanged"/> para
    /// cada losa de cada sistema del proyecto. Cuando una propiedad de input cambia,
    /// el engine vuelve a correr sobre esa losa y los outputs (HCalc, Qd, Qu, ...)
    /// se actualizan, lo que dispara los bindings del DataGrid.
    /// </summary>
    private void AdjuntarRecalculoEnVivo(Proyecto p)
    {
        foreach (var sistema in p.Sistemas)
        {
            foreach (var losa in sistema.Losas)
            {
                losa.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != null && _propsRecalcLosa.Contains(e.PropertyName))
                    {
                        CalculoEngine.RecalcularLosa(losa, sistema, p);
                    }
                };
            }
        }
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

    private Sistema? _sistemaActivo;
    /// <summary>
    /// Sistema/nivel actualmente seleccionado en la pestaña Niveles. Cuando
    /// cambia, el DataGrid central de losas se rebina automáticamente.
    /// </summary>
    public Sistema? SistemaActivo
    {
        get => _sistemaActivo;
        set { _sistemaActivo = value; OnPropertyChanged(); }
    }

    private ModoSidebar _modoActivo;
    /// <summary>
    /// Modo de la sidebar: determina qué contenido muestra el área principal.
    /// Default <see cref="ModoSidebar.Calculos"/> (las 4 pestañas del flujo de
    /// edición).
    /// </summary>
    public ModoSidebar ModoActivo
    {
        get => _modoActivo;
        set
        {
            if (_modoActivo != value)
            {
                _modoActivo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MostrarTopBarTabs));
            }
        }
    }

    /// <summary>
    /// Solo el modo Cálculos muestra los tabs Datos/Cargas/Niveles/Generar
    /// y los botones Guardar/Continuar del top bar.
    /// </summary>
    public bool MostrarTopBarTabs => _modoActivo == ModoSidebar.Calculos;

    private ProyectoResumen? _proyectoRecienteSeleccionado;
    /// <summary>Proyecto seleccionado en el panel lateral derecho del Explorador.</summary>
    public ProyectoResumen? ProyectoRecienteSeleccionado
    {
        get => _proyectoRecienteSeleccionado;
        set { _proyectoRecienteSeleccionado = value; OnPropertyChanged(); }
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

    /// <summary>
    /// Análogo a <see cref="ResolverTabInicialDesdeArgs"/> para el modo de la
    /// sidebar: <c>--modo=explorador|calculos|busqueda|plantillas|configuracion</c>.
    /// </summary>
    private ModoSidebar ResolverModoInicialDesdeArgs()
    {
        try
        {
            var args = System.Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg.StartsWith("--modo=", System.StringComparison.OrdinalIgnoreCase))
                {
                    var slug = arg.Substring(7).Trim();
                    if (System.Enum.TryParse<ModoSidebar>(slug, ignoreCase: true, out var modo))
                        return modo;
                }
            }
        }
        catch { /* fallback abajo */ }
        return ModoSidebar.Calculos;
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

        // ---- Sistemas / niveles ----
        // E1: replica las primeras losas reales del xls Neapolis IV (Carga
        // EARLLETTE filas 9-13). Sirven de fixture visual para que el usuario
        // vea el engine rellenar HCalc/Qd/Qu inmediatamente al arrancar.
        var e1 = new Sistema
        {
            Nombre = "E1",
            Uso    = SistemaUso.Entrepiso,
            CotaMetros = 2.80,
        };
        e1.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 6.45, Ly = 5.40, MampO = 1.78 });
        e1.Losas.Add(new Losa { Id = 2, Tipo = 33, Lx = 4.90, Ly = 4.45, MampO = 7.67 });
        e1.Losas.Add(new Losa { Id = 3, Tipo = 33, Lx = 4.90, Ly = 4.40 });
        e1.Losas.Add(new Losa { Id = 4, Tipo = 22, Lx = 3.80, Ly = 1.50 });
        e1.Losas.Add(new Losa { Id = 5, Tipo = 22, Lx = 1.20, Ly = 4.65 });

        // E2: copia simple de E1 para mostrar plurinivel.
        var e2 = new Sistema
        {
            Nombre = "E2",
            Uso    = SistemaUso.Entrepiso,
            CotaMetros = 5.60,
        };
        e2.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 6.45, Ly = 5.40, MampO = 1.78 });
        e2.Losas.Add(new Losa { Id = 2, Tipo = 33, Lx = 4.90, Ly = 4.45 });
        e2.Losas.Add(new Losa { Id = 3, Tipo = 33, Lx = 4.90, Ly = 4.40 });

        // Techo: nivel superior — uso=Techo cambia ql de 0.20 a 0.10.
        var techo = new Sistema
        {
            Nombre = "Techo",
            Uso    = SistemaUso.Techo,
            CotaMetros = 8.40,
        };
        techo.Losas.Add(new Losa { Id = 1, Tipo = 10, Lx = 6.45, Ly = 5.40 });
        techo.Losas.Add(new Losa { Id = 2, Tipo = 33, Lx = 4.90, Ly = 4.45 });

        p.Sistemas.Add(e1);
        p.Sistemas.Add(e2);
        p.Sistemas.Add(techo);

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

/// <summary>
/// Modos de navegación de la sidebar. Cada uno define el contenido del área
/// principal:
/// <list type="bullet">
///   <item><see cref="Explorador"/>: hub de proyectos (lista + detalle).</item>
///   <item><see cref="Busqueda"/>: búsqueda global (placeholder).</item>
///   <item><see cref="Calculos"/>: flujo de 4 tabs Datos/Cargas/Niveles/Generar.</item>
///   <item><see cref="Plantillas"/>: gestión de plantillas .docx (placeholder).</item>
///   <item><see cref="Configuracion"/>: ajustes globales (placeholder).</item>
/// </list>
/// </summary>
public enum ModoSidebar
{
    Explorador,
    Busqueda,
    Calculos,
    Plantillas,
    Configuracion,
}

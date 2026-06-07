using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LosasPlus.Calculo;
using LosasPlus.Generation;
using LosasPlus.Importers;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Services;
using LosasPlus.Validation;
using MemoriaPlus.Common;
using MemoriaPlus.Services;
using MemoriaPlus.Views;

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
        // ---- Bootstrap del registry de plantillas (commit 25) ----
        // Si es el primer arranque, registra la plantilla bundleada como
        // predeterminada para que el flujo de Generar no quede sin template.
        BootstrapPlantillaPredeterminada();

        // ---- Commands del top bar (se inicializan PRIMERO porque el setter de
        //      TabActivo invoca ContinuarCommand.RaiseCanExecuteChanged) ----
        ContinuarCommand          = new RelayCommand(Continuar,        PuedeContinuar);
        GuardarBorradorCommand    = new AsyncRelayCommand(GuardarBorrador);
        GuardarComoCommand        = new AsyncRelayCommand(GuardarComo);
        AbrirProyectoCommand      = new AsyncRelayCommand(AbrirProyecto);
        NuevoProyectoCommand      = new RelayCommand(() => _ = NuevoProyectoConfirmadoAsync());
        RestaurarCargasCommand    = new RelayCommand(RestaurarCargas);
        ImportarCargasXlsCommand  = new AsyncRelayCommand(ImportarCargasXls);
        ImportarTxtPerdomoCommand = new AsyncRelayCommand(ImportarTxtPerdomo, () => SistemaActivo != null);
        QuitarTxtPerdomoCommand   = new RelayCommand(QuitarTxtPerdomo,   () => SistemaActivo?.TieneSalidaPerdomo == true);
        GenerarMemoriaCommand     = new AsyncRelayCommand(GenerarMemoria);
        AbrirUltimaMemoriaCommand = new RelayCommand(AbrirUltimaMemoria, () => UltimoArchivoGenerado != null);
        AbrirProyectoRecienteCommand = new RelayCommand<string>(AbrirProyectoReciente);
        AgregarLosaCommand    = new RelayCommand(AgregarLosa,    () => SistemaActivo != null);
        AgregarSistemaCommand = new RelayCommand(AgregarSistema, () => ProyectoActivo != null);
        EliminarSistemaCommand = new AsyncRelayCommand(EliminarSistema, () => SistemaActivo != null);
        AbrirEnCalculosCommand = new RelayCommand(AbrirEnCalculos,
            () => ProyectoRecienteSeleccionado != null
                  && !string.IsNullOrEmpty(ProyectoRecienteSeleccionado.Path));
        IrABusquedaCommand = new RelayCommand(() => ModoActivo = ModoSidebar.Busqueda);

        // ---- Validación normativa (commit 21) ----
        Validacion = new ValidacionViewModel();

        // ---- Búsqueda global (commit 26) ----
        // Lambdas con suppress (!) — ProyectosRecientes y _proyectoActivo se
        // inicializan más abajo en este mismo ctor; las lambdas sólo se
        // ejecutan en respuesta a interacción del usuario, mucho después.
        Busqueda = new BusquedaViewModel(
            getProyectosRecientes: () => ProyectosRecientes!,
            getProyectoActivo:     () => _proyectoActivo!,
            abrirProyectoPorPath:  path => { AbrirProyectoReciente(path); ModoActivo = ModoSidebar.Calculos; },
            irASistema:            BuscarYActivarSistema,
            irALosa:               BuscarYActivarLosa);
        // Refresca IssuesEnSistemaActivo cuando el reporte se renueva (commit 22).
        Validacion.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ValidacionViewModel.Reporte))
            {
                OnPropertyChanged(nameof(IssuesEnSistemaActivo));
                OnPropertyChanged(nameof(HayIssuesEnSistemaActivo));
            }
        };

        // Refresca resultados de búsqueda cuando cambia el proyecto activo (commit 26).
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProyectoActivo)) Busqueda?.Refrescar();
        };

        // ---- Sidebar: lista de proyectos recientes (carga desde el registry real) ----
        ProyectosRecientes = new ObservableCollection<ProyectoResumen>();
        ProyectosRecientesFiltrados = new ObservableCollection<ProyectoResumen>();
        // RecargarProyectosRecientes() se llama abajo después de inicializar
        // los commands para evitar NRE en ObservableCollection.

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

        // ---- Primera validación normativa (commit 21) ----
        Validacion.RevalidarPara(ProyectoActivo);

        // ---- Modo de la sidebar (default Calculos para preservar el flujo) ----
        ModoActivo = ResolverModoInicialDesdeArgs();

        // ---- Cargar lista real de proyectos recientes desde el registry ----
        RecargarProyectosRecientes();

        // ---- Selección visible en la lista de proyectos recientes ----
        ProyectoRecienteSeleccionado = ProyectosRecientes.FirstOrDefault();

        // ---- Si se lanzó con un .lpx.json como arg (ej. desde LosasPlus
        //      "Generar memoria → MemoriaPlus"), abrir ese proyecto al iniciar. ----
        TryAbrirProyectoDesdeArgs();
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
                AdjuntarRecalculoLosa(losa, sistema, p);
        }
    }

    /// <summary>
    /// Suscribe el handler de recálculo a una losa individual. Extraído de
    /// <see cref="AdjuntarRecalculoEnVivo"/> para que el commando "+ Losa" pueda
    /// engancharlo sobre las losas recién creadas sin volver a recorrer todo.
    /// </summary>
    private void AdjuntarRecalculoLosa(Losa losa, Sistema sistema, Proyecto proyecto)
    {
        losa.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null && _propsRecalcLosa.Contains(e.PropertyName))
            {
                // Edición en vivo de una losa: chokepoint para marcar sucio.
                IsDirty = true;
                CalculoEngine.RecalcularLosa(losa, sistema, proyecto);
                Validacion?.Revalidar();
            }
        };
    }

    public ObservableCollection<ProyectoResumen> ProyectosRecientes { get; }

    /// <summary>
    /// Vista filtrada de <see cref="ProyectosRecientes"/> usada por el DataGrid
    /// del Explorador. Se re-genera al cambiar <see cref="BusquedaTexto"/> o
    /// al recargar el registry. La sidebar siempre muestra la lista completa.
    /// </summary>
    public ObservableCollection<ProyectoResumen> ProyectosRecientesFiltrados { get; }

    private string _busquedaTexto = "";
    /// <summary>
    /// Texto del filtro del Explorador. Filtra contra Nombre / Ingeniero / Codia
    /// (case-insensitive). Vacío = sin filtro.
    /// </summary>
    public string BusquedaTexto
    {
        get => _busquedaTexto;
        set
        {
            if (_busquedaTexto == value) return;
            _busquedaTexto = value ?? "";
            OnPropertyChanged();
            AplicarFiltroExplorador();
        }
    }

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
                OnPropertyChanged(nameof(CurrentView));
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
        set
        {
            _proyectoActivo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TituloVentana));
        }
    }

    private bool _isDirty;
    /// <summary>
    /// True cuando hay cambios sin guardar. Se marca en las mutaciones del
    /// proyecto (agregar/eliminar losa o sistema, importar, editar losas en
    /// vivo) y se limpia tras un Guardar/Abrir/Nuevo exitoso. Lo consume el
    /// handler <c>Closing</c> de la ventana para evitar la pérdida silenciosa
    /// de trabajo (Fase A — pérdida de datos).
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set { if (_isDirty == value) return; _isDirty = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Marca el proyecto como modificado. Método público para que las vistas y
    /// el chokepoint de edición en vivo lo invoquen sin exponer el setter.
    /// </summary>
    public void MarcarSucio() => IsDirty = true;

    private Sistema? _sistemaActivo;
    /// <summary>
    /// Sistema/nivel actualmente seleccionado en la pestaña Niveles. Cuando
    /// cambia, el DataGrid central de losas se rebina automáticamente y los
    /// commands del panel F. Perdomo refrescan su CanExecute.
    /// </summary>
    public Sistema? SistemaActivo
    {
        get => _sistemaActivo;
        set
        {
            _sistemaActivo = value;
            OnPropertyChanged();
            ImportarTxtPerdomoCommand?.RaiseCanExecuteChanged();
            QuitarTxtPerdomoCommand?.RaiseCanExecuteChanged();
            AgregarLosaCommand?.RaiseCanExecuteChanged();
            EliminarSistemaCommand?.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(IssuesEnSistemaActivo));
            OnPropertyChanged(nameof(HayIssuesEnSistemaActivo));
        }
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
                OnPropertyChanged(nameof(CurrentView));
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
        set
        {
            _proyectoRecienteSeleccionado = value;
            OnPropertyChanged();
            AbrirEnCalculosCommand?.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand ContinuarCommand          { get; }
    public AsyncRelayCommand GuardarBorradorCommand    { get; }
    public AsyncRelayCommand GuardarComoCommand        { get; }
    public AsyncRelayCommand AbrirProyectoCommand      { get; }
    public RelayCommand NuevoProyectoCommand      { get; }
    public RelayCommand RestaurarCargasCommand    { get; }
    public AsyncRelayCommand ImportarCargasXlsCommand  { get; }
    public AsyncRelayCommand ImportarTxtPerdomoCommand { get; }
    public RelayCommand QuitarTxtPerdomoCommand   { get; }
    public AsyncRelayCommand GenerarMemoriaCommand     { get; }
    public RelayCommand AbrirUltimaMemoriaCommand { get; }
    public RelayCommand<string> AbrirProyectoRecienteCommand { get; }
    public RelayCommand AgregarLosaCommand    { get; }
    public RelayCommand AgregarSistemaCommand { get; }
    public AsyncRelayCommand EliminarSistemaCommand { get; }
    public RelayCommand AbrirEnCalculosCommand { get; }
    public RelayCommand IrABusquedaCommand    { get; }

    /// <summary>
    /// Sub-VM que mantiene el reporte de validación normativa y los conteos
    /// del chip indicador. Se re-valida en cada cambio del proyecto o de
    /// las losas (sin debounce — el engine es trivial).
    /// </summary>
    public ValidacionViewModel Validacion { get; }

    /// <summary>
    /// Sub-VM del modo Búsqueda Global (commit 26). Filtra simultáneamente
    /// sobre proyectos recientes, sistemas y losas del proyecto activo. Sus
    /// callbacks de navegación delegan en este MainViewModel.
    /// </summary>
    public BusquedaViewModel Busqueda { get; }

    /// <summary>
    /// Cambia el SistemaActivo al sistema con el nombre dado y entra al modo
    /// Cálculos en la pestaña Niveles. Usado por la navegación desde
    /// Búsqueda Global cuando el usuario clickea un resultado tipo "Nivel".
    /// </summary>
    private void BuscarYActivarSistema(string nombreSistema)
    {
        if (ProyectoActivo is null) return;
        var sistema = ProyectoActivo.Sistemas.FirstOrDefault(s => s.Nombre == nombreSistema);
        if (sistema is null) return;
        SistemaActivo = sistema;
        ModoActivo = ModoSidebar.Calculos;
        var tabNiveles = Tabs.FirstOrDefault(t => t.Slug == "Niveles");
        if (tabNiveles is not null) TabActivo = tabNiveles;
    }

    /// <summary>
    /// Análogo a <see cref="BuscarYActivarSistema"/> pero también guarda el
    /// id de la losa como contexto. La selección visual del DataGrid en
    /// Niveles no se mueve automáticamente — un futuro commit puede agregar
    /// scroll-to-row.
    /// </summary>
    private void BuscarYActivarLosa(string nombreSistema, int losaId)
    {
        BuscarYActivarSistema(nombreSistema);
        // TODO: cuando exista LosaSeleccionada en SistemaActivo, asignarla aquí.
    }

    /// <summary>
    /// Conteo de issues que afectan al SistemaActivo. Usado por el banner
    /// amarillo arriba del DataGrid en NivelesView (commit 22).
    /// </summary>
    public int IssuesEnSistemaActivo => Validacion.ContarIssuesDeSistema(SistemaActivo?.Nombre);
    public bool HayIssuesEnSistemaActivo => IssuesEnSistemaActivo > 0;

    // ----- Estado de la pestaña Generar -----

    private string? _ultimoArchivoGenerado;
    /// <summary>Path del .docx producido por la última generación exitosa.</summary>
    public string? UltimoArchivoGenerado
    {
        get => _ultimoArchivoGenerado;
        private set
        {
            _ultimoArchivoGenerado = value;
            OnPropertyChanged();
            AbrirUltimaMemoriaCommand.RaiseCanExecuteChanged();
        }
    }

    private string _statusGeneracion = "";
    /// <summary>Mensaje de status de la última generación (éxito o error).</summary>
    public string StatusGeneracion
    {
        get => _statusGeneracion;
        private set { _statusGeneracion = value; OnPropertyChanged(); OnPropertyChanged(nameof(GeneracionExitosa)); OnPropertyChanged(nameof(GeneracionConError)); }
    }

    private bool _generacionExito;
    public bool GeneracionExitosa  => _generacionExito  && !string.IsNullOrEmpty(_statusGeneracion);
    public bool GeneracionConError => !_generacionExito && !string.IsNullOrEmpty(_statusGeneracion);

    private int _ultimasSustituciones;
    public int UltimasSustituciones
    {
        get => _ultimasSustituciones;
        private set { _ultimasSustituciones = value; OnPropertyChanged(); }
    }

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

    public string Version => "v0.5.0 — Memoria Plus";

    /// <summary>
    /// Copyright dinámico: usa el año en curso para que el footer no quede
    /// desfasado con el paso del tiempo. Si en el futuro la app pasa a un año
    /// nuevo, el statusbar se actualiza solo al próximo arranque.
    /// </summary>
    public string CopyrightTexto => $"© {DateTime.Now.Year} Memoria Plus Engineering";

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
    /// <summary>
    /// Si se pasó un path posicional <c>.lpx.json</c> como argumento al
    /// arrancar (ej. desde LosasPlus.exe vía "Generar memoria"), abre ese
    /// proyecto automáticamente. Solo procesa argumentos que NO empiezan
    /// con <c>--</c> (esos son flags) y que terminan en <c>.lpx.json</c>
    /// o <c>.json</c>.
    /// </summary>
    private void TryAbrirProyectoDesdeArgs()
    {
        try
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)  // skip [0] = exe path
            {
                var a = args[i];
                if (string.IsNullOrWhiteSpace(a)) continue;
                if (a.StartsWith("--")) continue;
                if ((a.EndsWith(".lpx.json", System.StringComparison.OrdinalIgnoreCase)
                     || a.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                    && File.Exists(a))
                {
                    CargarProyectoDesdeArchivo(a);
                    return;
                }
            }
        }
        catch { /* no bloqueante */ }
    }

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

    /// <summary>
    /// Guarda el <see cref="ProyectoActivo"/> en <see cref="Proyecto.Archivo"/>
    /// si está set; si no, delega en <see cref="GuardarComo"/>.
    /// </summary>
    private async Task GuardarBorrador() => await GuardarAsync();

    /// <summary>
    /// Guarda el proyecto activo y devuelve <c>true</c> si tuvo éxito. Si aún no
    /// tiene archivo, delega en <see cref="GuardarComoAsync"/>. Lo consume el
    /// handler <c>Closing</c> de la ventana (Fase A): solo cierra si guardó.
    /// Limpia <see cref="IsDirty"/> al tener éxito.
    /// </summary>
    public async Task<bool> GuardarAsync()
    {
        if (string.IsNullOrEmpty(ProyectoActivo?.Archivo))
            return await GuardarComoAsync();
        try
        {
            ProyectoSerializer.Save(ProyectoActivo, ProyectoActivo.Archivo);
            ActualizarRecents();
            IsDirty = false;
            StatusPersistencia = $"Guardado: {Path.GetFileName(ProyectoActivo.Archivo)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusPersistencia = $"Error al guardar: {ex.Message}";
            return false;
        }
    }

    /// <summary>Pregunta destino y guarda. Si el usuario cancela, no-op.</summary>
    private async Task GuardarComo() => await GuardarComoAsync();

    /// <summary>
    /// Pregunta destino y guarda. Devuelve <c>true</c> si se eligió destino y el
    /// guardado fue exitoso; <c>false</c> si canceló o falló. Limpia
    /// <see cref="IsDirty"/> al tener éxito.
    /// </summary>
    public async Task<bool> GuardarComoAsync()
    {
        if (ProyectoActivo is null) return false;
        var ruta = await AppServices.Dialogs.SaveFileAsync(
            "Guardar proyecto Memoria Plus", SugerirNombreProyecto(), ProyectoSerializer.Extension,
            new FileFilter("Proyecto Memoria Plus", new[] { "*.lpx.json" }),
            new FileFilter("JSON", new[] { "*.json" }));
        if (ruta is null) return false;

        try
        {
            ProyectoSerializer.Save(ProyectoActivo, ruta);
            ProyectoActivo.Archivo = ruta;
            ActualizarRecents();
            IsDirty = false;
            StatusPersistencia = $"Guardado: {Path.GetFileName(ruta)}";
            OnPropertyChanged(nameof(TituloVentana));
            return true;
        }
        catch (Exception ex)
        {
            StatusPersistencia = $"Error al guardar: {ex.Message}";
            return false;
        }
    }

    /// <summary>Abre un file picker y carga el .lpx.json elegido.</summary>
    private async Task AbrirProyecto()
    {
        if (!await ConfirmarDescartarSiSucioAsync()) return;
        var ruta = await AppServices.Dialogs.OpenFileAsync(
            "Abrir proyecto Memoria Plus",
            new FileFilter("Proyecto Memoria Plus", new[] { "*.lpx.json" }),
            new FileFilter("JSON", new[] { "*.json" }),
            new FileFilter("Todos", new[] { "*.*" }));
        if (ruta is null) return;
        CargarProyectoDesdeArchivo(ruta);
    }

    /// <summary>Carga un proyecto desde un path (usado por Abrir y por proyectos recientes).</summary>
    private void CargarProyectoDesdeArchivo(string path)
    {
        try
        {
            var p = ProyectoSerializer.Load(path);
            ProyectoActivo = p;
            // Re-suscribir el handler de recálculo en vivo a las losas nuevas.
            AdjuntarRecalculoEnVivo(p);
            // Sistema activo: primero por defecto.
            SistemaActivo = p.Sistemas.FirstOrDefault();
            Validacion.RevalidarPara(p);
            ActualizarRecents();
            IsDirty = false;
            StatusPersistencia = $"Cargado: {Path.GetFileName(path)}";
            OnPropertyChanged(nameof(TituloVentana));
        }
        catch (Exception ex)
        {
            StatusPersistencia = $"Error al abrir: {ex.Message}";
        }
    }

    /// <summary>
    /// Crea un Proyecto vacío con cargas semilla y precarga los campos de autor
    /// con el <see cref="PerfilIngeniero"/> guardado por el usuario (si existe).
    /// Reemplaza el ProyectoActivo.
    /// </summary>
    /// <summary>
    /// Si hay cambios sin guardar, pide confirmación de 3 estados antes de
    /// descartarlos (Fase A — pérdida de datos). Devuelve <c>true</c> si se puede
    /// continuar con la operación destructiva (Nuevo/Abrir), <c>false</c> si el
    /// usuario abortó. Sin cambios sucios ⇒ continúa sin preguntar.
    /// <list type="bullet">
    /// <item><c>Cancelar</c> (también el descarte del diálogo: Escape / X) ⇒
    /// abortar (no descarta).</item>
    /// <item><c>Guardar</c> ⇒ guarda primero; sólo continúa si el guardado fue
    /// exitoso.</item>
    /// <item><c>Descartar</c> ⇒ continúa descartando los cambios.</item>
    /// </list>
    /// </summary>
    public async Task<bool> ConfirmarDescartarSiSucioAsync()
    {
        if (!IsDirty) return true;
        var r = await AppServices.MessageBox.ConfirmarGuardarDescartarCancelarAsync(
            "Cambios sin guardar",
            "Hay cambios sin guardar.\n\n¿Querés guardar los cambios antes de continuar?");
        if (r == ResultadoDescarte.Cancelar) return false;   // abortar
        if (r == ResultadoDescarte.Guardar)
            return await GuardarAsync();                      // sólo continúa si guardó bien
        return true;                                          // Descartar: continuar
    }

    /// <summary>Nuevo proyecto con guardia de cambios sin guardar (cableado al comando).</summary>
    public async Task NuevoProyectoConfirmadoAsync()
    {
        if (!await ConfirmarDescartarSiSucioAsync()) return;
        NuevoProyecto();
    }

    private void NuevoProyecto()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre = "Proyecto sin título";

        // Precarga del perfil del ingeniero (commit 19): si el usuario configuró
        // sus datos en Configuración → Datos del ingeniero, se aplican aquí.
        if (PerfilIngenieroService.Existe())
        {
            var perfil = PerfilIngenieroService.Load();
            if (!string.IsNullOrWhiteSpace(perfil.Nombre))           p.Autor = perfil.Nombre;
            if (!string.IsNullOrWhiteSpace(perfil.Codia))            p.Codia = perfil.Codia;
            if (!string.IsNullOrWhiteSpace(perfil.TelefonoFijo))     p.TelefonoFijo = perfil.TelefonoFijo;
            if (!string.IsNullOrWhiteSpace(perfil.TelefonoCelular))  p.TelefonoCelular = perfil.TelefonoCelular;
            if (!string.IsNullOrWhiteSpace(perfil.Ciudad))           p.Ciudad = perfil.Ciudad;
        }

        ProyectoActivo = p;
        AdjuntarRecalculoEnVivo(p);
        SistemaActivo = null;  // proyecto nuevo sin sistemas
        Validacion.RevalidarPara(p);
        IsDirty = false;
        StatusPersistencia = "Nuevo proyecto creado.";
        OnPropertyChanged(nameof(TituloVentana));
    }

    /// <summary>
    /// Agrega una nueva <see cref="Losa"/> al <see cref="SistemaActivo"/>. El Id
    /// se calcula como <c>max(Losas.Id) + 1</c> para no chocar con existentes.
    /// La losa se crea con los defaults del modelo (Tipo=10, Lx=Ly=4.000 m) y
    /// se le adjunta el handler de recálculo en vivo. Inmediatamente se corre
    /// el engine sobre ella para rellenar HCalc/Qd/Qu antes de que el usuario
    /// edite nada.
    /// </summary>
    private void AgregarLosa()
    {
        if (SistemaActivo is null || ProyectoActivo is null) return;
        var siguienteId = SistemaActivo.Losas.Count == 0
            ? 1
            : SistemaActivo.Losas.Max(l => l.Id) + 1;
        var nueva = new Losa { Id = siguienteId };
        SistemaActivo.Losas.Add(nueva);
        AdjuntarRecalculoLosa(nueva, SistemaActivo, ProyectoActivo);
        CalculoEngine.RecalcularLosa(nueva, SistemaActivo, ProyectoActivo);
        Validacion.Revalidar();
        IsDirty = true;
        StatusPersistencia = $"Losa L{siguienteId} agregada al nivel {SistemaActivo.Nombre}.";
    }

    /// <summary>
    /// Agrega un nuevo <see cref="Sistema"/> al proyecto. Nombre auto-generado
    /// como <c>E{n+1}</c> donde n es el count actual; cota = última cota +
    /// 2.80 m (un nivel típico) y uso = Entrepiso. El usuario puede editar
    /// estos defaults en la pestaña.
    /// </summary>
    private void AgregarSistema()
    {
        if (ProyectoActivo is null) return;
        var n = ProyectoActivo.Sistemas.Count;
        var nuevoNombre = $"E{n + 1}";
        var ultimaCota = ProyectoActivo.Sistemas.LastOrDefault()?.CotaMetros ?? 0.0;
        var nuevo = new Sistema
        {
            Nombre = nuevoNombre,
            Uso = SistemaUso.Entrepiso,
            CotaMetros = ultimaCota + 2.80,
        };
        ProyectoActivo.Sistemas.Add(nuevo);
        SistemaActivo = nuevo;  // foco automático al nuevo nivel
        IsDirty = true;
        StatusPersistencia = $"Nivel {nuevoNombre} agregado.";
    }

    /// <summary>
    /// Elimina el <see cref="SistemaActivo"/> del proyecto, previa confirmación
    /// del usuario (la operación destruye las losas del nivel y su salida .txt
    /// importada). Tras eliminar, reselecciona un nivel vecino — el que ocupa
    /// ahora ese índice, o <c>null</c> si el proyecto quedó sin niveles.
    /// </summary>
    private async Task EliminarSistema()
    {
        if (ProyectoActivo is null || SistemaActivo is null) return;
        var sistema = SistemaActivo;

        var detalle = $"Se perderán sus {sistema.Losas.Count} losa(s)";
        if (sistema.TieneSalidaPerdomo)
            detalle += " y la salida .txt importada de F. Perdomo";

        var confirmar = await AppServices.MessageBox.ConfirmYesNoAsync(
            "Eliminar nivel",
            $"¿Eliminar el nivel «{sistema.Nombre}»?\n\n{detalle}.");
        if (!confirmar) return;

        int idx = ProyectoActivo.Sistemas.IndexOf(sistema);
        ProyectoActivo.Sistemas.Remove(sistema);

        // Reseleccionar un nivel vecino para que la pestaña no quede colgada.
        SistemaActivo = ProyectoActivo.Sistemas.Count == 0
            ? null
            : ProyectoActivo.Sistemas[Math.Min(idx, ProyectoActivo.Sistemas.Count - 1)];

        IsDirty = true;
        StatusPersistencia = $"Nivel {sistema.Nombre} eliminado.";
    }

    /// <summary>Abre un proyecto desde el sidebar de recientes (click en una entry).</summary>
    private void AbrirProyectoReciente(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path))
        {
            ProyectoRegistry.Remove(path);
            RecargarProyectosRecientes();
            StatusPersistencia = $"El archivo ya no existe: {Path.GetFileName(path)}";
            return;
        }
        // Guardia de cambios sin guardar (Fase A) antes de reemplazar el activo.
        _ = AbrirRecienteConfirmadoAsync(path);
    }

    private async Task AbrirRecienteConfirmadoAsync(string path)
    {
        if (!await ConfirmarDescartarSiSucioAsync()) return;
        CargarProyectoDesdeArchivo(path);
    }

    private void ActualizarRecents()
    {
        if (ProyectoActivo is null || string.IsNullOrEmpty(ProyectoActivo.Archivo)) return;
        ProyectoRegistry.AddOrUpdate(
            ProyectoActivo.Archivo,
            ProyectoActivo.Nombre,
            ProyectoActivo.Autor,
            ProyectoActivo.Codia,
            ProyectoActivo.Sistemas.Count);
        RecargarProyectosRecientes();
    }

    private void RecargarProyectosRecientes()
    {
        ProyectosRecientes.Clear();
        foreach (var e in ProyectoRegistry.Load())
        {
            ProyectosRecientes.Add(new ProyectoResumen(
                e.NombreProyecto,
                e.Ingeniero,
                e.Codia,
                e.CantidadNiveles,
                e.UltimoAccesoUtc.ToLocalTime().ToString("dd/MM/yy"),
                "Guardado",
                e.Path));
        }
        AplicarFiltroExplorador();
    }

    /// <summary>
    /// Re-puebla <see cref="ProyectosRecientesFiltrados"/> aplicando el texto
    /// de <see cref="BusquedaTexto"/> sobre los campos Nombre / Ingeniero /
    /// Codia (case-insensitive). Vacío = lista completa.
    /// </summary>
    private void AplicarFiltroExplorador()
    {
        ProyectosRecientesFiltrados.Clear();
        var q = _busquedaTexto.Trim();
        IEnumerable<ProyectoResumen> resultados = ProyectosRecientes;
        if (!string.IsNullOrEmpty(q))
        {
            resultados = ProyectosRecientes.Where(p =>
                p.Nombre.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Ingeniero.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Codia.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var p in resultados) ProyectosRecientesFiltrados.Add(p);
        Busqueda?.Refrescar();
    }

    /// <summary>
    /// Abre el proyecto seleccionado del Explorador y cambia el modo a Cálculos
    /// para entrar al flujo de edición. Usado por el botón "📂 Abrir en
    /// Cálculos" del panel detalle y por doble-click en una fila.
    /// </summary>
    private void AbrirEnCalculos()
    {
        var resumen = ProyectoRecienteSeleccionado;
        if (resumen is null || string.IsNullOrEmpty(resumen.Path)) return;
        AbrirProyectoReciente(resumen.Path);
        ModoActivo = ModoSidebar.Calculos;
    }

    private string SugerirNombreProyecto()
    {
        var slug = (ProyectoActivo?.Nombre ?? "Proyecto")
            .Replace(' ', '_').Replace('/', '-').Replace('\\', '-');
        return $"{slug}{ProyectoSerializer.Extension}";
    }

    private string _statusPersistencia = "";
    /// <summary>Mensaje breve del último resultado de persistencia (guardado/cargado/error).</summary>
    public string StatusPersistencia
    {
        get => _statusPersistencia;
        private set { _statusPersistencia = value; OnPropertyChanged(); }
    }

    /// <summary>Título del Window — incluye el path del archivo si está guardado.</summary>
    public string TituloVentana =>
        ProyectoActivo is null
            ? "Memoria Plus"
            : string.IsNullOrEmpty(ProyectoActivo.Archivo)
                ? $"Memoria Plus — {ProyectoActivo.Nombre} (sin guardar)"
                : $"Memoria Plus — {Path.GetFileName(ProyectoActivo.Archivo)}";

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
    /// Abre un <see cref="Microsoft.Win32.OpenFileDialog"/> para elegir un libro
    /// Excel (formato del ingeniero, hoja <c>Cargas</c>), corre el
    /// <see cref="CargasGlobalesXlsxImporter"/>, y reemplaza
    /// <see cref="Proyecto.Cargas"/> con el resultado. Errores se reportan en
    /// <see cref="StatusImportarCargas"/>.
    /// </summary>
    private async Task ImportarCargasXls()
    {
        try
        {
            var ruta = await AppServices.Dialogs.OpenFileAsync(
                "Importar cargas globales desde Excel",
                new FileFilter("Libro Excel", new[] { "*.xlsx" }),
                new FileFilter("Libro Excel 97-2003", new[] { "*.xls" }));
            if (ruta is null)
            {
                StatusImportarCargas = "";
                return;
            }

            var importer = new CargasGlobalesXlsxImporter();
            var nuevas = importer.Importar(ruta);
            ProyectoActivo.Cargas = nuevas;
            IsDirty = true;
            StatusImportarCargas = $"Cargas importadas desde {Path.GetFileName(ruta)}: " +
                                   $"{nuevas.PesosPropiosEntrepiso.Items.Count} pesos propios entrepiso, " +
                                   $"{nuevas.PesosPropiosTecho.Items.Count} pesos propios techo, " +
                                   $"{nuevas.CargaMuertaPorEspesor.Filas.Count} filas en tabla h↔qd.";
        }
        catch (Exception ex)
        {
            StatusImportarCargas = $"Error importando .xls: {ex.Message}";
        }
    }

    private string _statusImportarCargas = "";
    /// <summary>Mensaje del último intento de importar cargas (éxito o error). Se limpia al cancelar el dialog.</summary>
    public string StatusImportarCargas
    {
        get => _statusImportarCargas;
        private set { _statusImportarCargas = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Abre un <see cref="Microsoft.Win32.OpenFileDialog"/> para elegir el
    /// <c>.txt</c> de salida de Losas.exe (F. Perdomo) y lo asocia al
    /// <see cref="SistemaActivo"/>. El parser respeta el encoding cp1252 que
    /// produce el motor original.
    /// </summary>
    private async Task ImportarTxtPerdomo()
    {
        if (SistemaActivo is null) return;
        try
        {
            var ruta = await AppServices.Dialogs.OpenFileAsync(
                $"Importar salida F. Perdomo — Nivel {SistemaActivo.Nombre}",
                new FileFilter("Salida Losas.exe", new[] { "*.txt", "*.TXT" }),
                new FileFilter("Todos los archivos", new[] { "*.*" }));
            if (ruta is null)
            {
                StatusImportarTxt = "";
                return;
            }

            var idsEsperados = SistemaActivo.Losas.Select(l => l.Id);
            var salida = SalidaPerdomoAdapter.FromFile(ruta, idsEsperados);
            SistemaActivo.SalidaPerdomo = salida;
            IsDirty = true;

            var huerfanas = salida.LosasNoParseadas.Count;
            StatusImportarTxt = huerfanas == 0
                ? $".txt importado: {salida.Momentos.Count} losas con momentos, " +
                  $"{salida.ArmadurasXCentro.Count}+{salida.ArmadurasYCentro.Count} armaduras de vano, " +
                  $"{salida.ArmadurasXApoyos.Count}+{salida.ArmadurasYApoyos.Count} sobre apoyos."
                : $".txt importado con {huerfanas} losa(s) sin parsear: " +
                  string.Join(", ", salida.LosasNoParseadas.Select(id => $"L{id}"));

            QuitarTxtPerdomoCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(SistemaActivo));  // refresca bindings dependientes
        }
        catch (Exception ex)
        {
            StatusImportarTxt = $"Error importando .txt: {ex.Message}";
        }
    }

    /// <summary>Desasocia el .txt del SistemaActivo (vuelve a "Sin .txt importado").</summary>
    private void QuitarTxtPerdomo()
    {
        if (SistemaActivo?.SalidaPerdomo is null) return;
        SistemaActivo.SalidaPerdomo = null;
        IsDirty = true;
        StatusImportarTxt = "";
        QuitarTxtPerdomoCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SistemaActivo));
    }

    private string _statusImportarTxt = "";
    /// <summary>Mensaje del último intento de importar el .txt F. Perdomo del nivel activo.</summary>
    public string StatusImportarTxt
    {
        get => _statusImportarTxt;
        private set { _statusImportarTxt = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Genera el .docx de la memoria con los datos de <see cref="ProyectoActivo"/>.
    /// Abre un <see cref="Microsoft.Win32.SaveFileDialog"/> para que el usuario
    /// elija el destino, llama al <see cref="MemoriaGenerator"/>, y deja el
    /// status visible en <see cref="StatusGeneracion"/> + path en
    /// <see cref="UltimoArchivoGenerado"/>.
    /// </summary>
    private async Task GenerarMemoria()
    {
        try
        {
            var plantilla = ResolverPlantillaPath();
            if (plantilla is null)
            {
                StatusGeneracion = "Plantilla no encontrada en Resources/templates/.";
                _generacionExito = false;
                return;
            }

            var ruta = await AppServices.Dialogs.SaveFileAsync(
                "Guardar memoria de cálculo", SugerirNombreArchivo(), ".docx",
                new FileFilter("Documento Word", new[] { "*.docx" }));
            if (ruta is null)
            {
                StatusGeneracion = "";  // usuario canceló — limpiar status
                return;
            }

            var gen = new MemoriaGenerator();
            var reporte = gen.Generar(ProyectoActivo, plantilla, ruta);

            UltimoArchivoGenerado = reporte.OutputPath;
            UltimasSustituciones  = reporte.SustitucionesAplicadas;
            _generacionExito = reporte.Exito;
            if (reporte.Exito)
            {
                StatusGeneracion = $"Memoria generada con {reporte.SustitucionesAplicadas} sustituciones. " +
                                   $"Archivo: {Path.GetFileName(reporte.OutputPath)}";
            }
            else
            {
                StatusGeneracion = "Generada con advertencias: " +
                                   $"{reporte.PlaceholdersNoSustituidos.Count} placeholder(s) sin sustituir " +
                                   $"({string.Join(", ", reporte.PlaceholdersNoSustituidos)}).";
            }
        }
        catch (Exception ex)
        {
            UltimoArchivoGenerado = null;
            _generacionExito = false;
            StatusGeneracion = $"Error generando memoria: {ex.Message}";
        }
    }

    private void AbrirUltimaMemoria()
    {
        if (UltimoArchivoGenerado is null || !File.Exists(UltimoArchivoGenerado)) return;
        try
        {
            AppServices.Launcher.OpenInDefaultApp(UltimoArchivoGenerado);
        }
        catch (Exception ex)
        {
            StatusGeneracion = $"No se pudo abrir el archivo: {ex.Message}";
            _generacionExito = false;
        }
    }

    /// <summary>
    /// Encuentra la plantilla a usar para generar la memoria. Prioriza la
    /// predeterminada del <see cref="PlantillaRegistry"/>; si el registry está
    /// vacío o la entrada apunta a un archivo inexistente, cae a la plantilla
    /// bundleada con la app
    /// (<c>{AppBaseDir}/Resources/templates/Memoria_Losas_PLANTILLA.docx</c>).
    /// </summary>
    private static string? ResolverPlantillaPath()
    {
        var pred = PlantillaRegistry.GetPredeterminada();
        if (pred is not null && File.Exists(pred.Path)) return pred.Path;

        var bundled = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "templates", "Memoria_Losas_PLANTILLA.docx");
        return File.Exists(bundled) ? bundled : null;
    }

    /// <summary>
    /// Si el registry de plantillas está vacío al primer arranque, registra la
    /// plantilla bundleada como predeterminada. Idempotente — no hace nada en
    /// arranques posteriores. Llamado desde el constructor de MainViewModel
    /// (commit 25).
    /// </summary>
    private static void BootstrapPlantillaPredeterminada()
    {
        var bundled = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "templates", "Memoria_Losas_PLANTILLA.docx");
        try { PlantillaRegistry.BootstrapIfNeeded(bundled); } catch { /* no bloqueante */ }
    }

    /// <summary>Sugiere un nombre tipo <c>Torre_Sol_Memoria_07-05-2026.docx</c>.</summary>
    private string SugerirNombreArchivo()
    {
        var slug = (ProyectoActivo?.Nombre ?? "Memoria")
            .Replace(' ', '_').Replace('/', '-').Replace('\\', '-');
        var fecha = DateTime.Now.ToString("dd-MM-yyyy");
        return $"{slug}_Memoria_{fecha}.docx";
    }

    // -------------------------------------------------------------
    // Semilla de proyecto de ejemplo (Torre Residencial Ensanche Piantini)
    // Los valores espejean el screenshot de Stitch para verificar bindings.
    // -------------------------------------------------------------

    private static Proyecto SeedProyectoEjemplo()
    {
        // ProyectoFactory para que Cargas arranque seedeada
        // (SemillaPorDefecto: 15 filas h, 3 pesos propios entrepiso, etc.).
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre                     = "Torre Residencial Ensanche Piantini";
        p.UbicacionCompleta          = "Av. Abraham Lincoln esq. Lope de Vega";
        p.Ciudad                     = "Santo Domingo";
        p.MesAno                     = "10/2023";
        p.Uso                        = "Residencial";
        p.CantidadNiveles            = 12;
        p.SistemaEstructural         = "Aporticado";
        p.Autor                      = "Ing. Rafael Gómez";
        p.Codia                      = "15428";
        p.TelefonoFijo               = "(809) 555-0123";
        p.TelefonoCelular            = "(809) 555-4567";
        p.DisenadorArquitectonico    = "Arq. Luis Sánchez";
        p.TipoFundaciones            = "Zapatas aisladas";
        p.EsfuerzoAdmisible          = 2.5;
        p.ProfundidadDesplante       = 1.5;
        p.FcKgCm2                    = 280;
        p.FyKgCm2                    = 4200;
        p.OtrosParametros            = "Suelo tipo C según R-001. Capacidad portante verificada por estudio geotécnico de junio 2023.";
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

    // =====================================================================
    // Router de vistas (reemplaza el MultiDataTrigger de WPF)
    // =====================================================================
    private readonly Dictionary<string, Avalonia.Controls.Control> _vistaCache = new();

    /// <summary>
    /// Vista activa según <see cref="ModoActivo"/> / <see cref="TabActivo"/>,
    /// cacheada por clave. El <c>ContentControl</c> de MainWindow enlaza su
    /// Content a esta propiedad; los setters de ModoActivo/TabActivo notifican
    /// el cambio. Las vistas heredan el DataContext (este VM) del ContentControl,
    /// salvo Plantillas/Configuración que crean su propio sub-VM.
    /// </summary>
    public Avalonia.Controls.Control CurrentView
    {
        get
        {
            var key = ModoActivo == ModoSidebar.Calculos
                ? (_tabActivo?.Slug ?? "DatosGenerales")
                : ModoActivo.ToString();
            if (!_vistaCache.TryGetValue(key, out var vista))
            {
                vista = CrearVista(key);
                _vistaCache[key] = vista;
            }
            return vista;
        }
    }

    private static Avalonia.Controls.Control CrearVista(string key) => key switch
    {
        "DatosGenerales" => new DatosGeneralesView(),
        "Cargas"         => new CargasView(),
        "Niveles"        => new NivelesView(),
        "Generar"        => new GenerarView(),
        "Explorador"     => new ExploradorView(),
        "Busqueda"       => new BusquedaView(),
        "Plantillas"     => new PlantillasView(),
        "Configuracion"  => new ConfiguracionView(),
        _                => new ComingSoonView { Tag = key },
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Resumen de un proyecto en la lista lateral / hub.</summary>
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

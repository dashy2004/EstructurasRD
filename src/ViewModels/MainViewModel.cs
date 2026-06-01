using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using LosasPlus.Generation;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Services;
using LosasPlus.Validation;
using LosasPlus.ViewModels.Vigas;
using MemoriaPlusVm = MemoriaPlus.ViewModels;  // ProyectoResumen vive en src.UI.Shared

namespace LosasPlus.ViewModels;

public class MainViewModel : INotifyPropertyChanged, MemoriaPlusVm.IValidacionHost, MemoriaPlusVm.IBusquedaHost
{
    private readonly Proyecto _proyecto = new();
    private Sistema _sistemaActivo = NuevoSistemaDemo();
    private string _losasExePath = "";
    private string? _dlPath;
    private string? _txtPath;
    private string _txtContent = "";
    private string _dlContent = "";
    private string _logText = "";
    private string _filtroTipo = "";
    private bool _ocupado;
    private ModoSidebar _modoActivo = ModoSidebar.Editor;

    // ---- Shell moderno (commit 31) -------------------------------------

    /// <summary>
    /// Modo activo de la sidebar principal. Determina qué contenido muestra
    /// el área principal: Editor (tabla de losas), DLEditor crudo,
    /// Salida del .TXT, Reglamento R-001, Plugins, Acerca.
    ///
    /// <para>
    /// Antes del commit 31 estos modos vivían como TabItems del TabControl
    /// vertical. La modernización los promueve a "modos" tipo MemoriaPlus
    /// para alinear el shell de ambas apps.
    /// </para>
    /// </summary>
    public ModoSidebar ModoActivo
    {
        get => _modoActivo;
        set
        {
            if (_modoActivo == value) return;
            _modoActivo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EsModoEditor));
        }
    }

    /// <summary>Conveniencia para DataTriggers que muestran el top bar de acciones solo en Editor.</summary>
    public bool EsModoEditor => _modoActivo == ModoSidebar.Editor;

    /// <summary>Texto de versión mostrado en el branding de la sidebar.</summary>
    public string Version => "v0.5.0 — LosasPlus";

    /// <summary>Copyright dinámico (año en curso) — bound al statusbar.</summary>
    public string CopyrightTexto => $"© {DateTime.Now.Year} LosasPlus · motor: F. Perdomo (Pieper-Martens)";

    /// <summary>
    /// Título dinámico del Window. Refleja el nombre del proyecto activo y
    /// si está guardado o no. Bound a Window.Title via {Binding TituloVentana}.
    /// </summary>
    public string TituloVentana
    {
        get
        {
            var nombre = string.IsNullOrWhiteSpace(_proyecto.Nombre) ? "(sin nombre)" : _proyecto.Nombre;
            if (string.IsNullOrEmpty(_proyecto.Archivo))
                return $"LosasPlus · {nombre} · sin guardar";
            return $"LosasPlus · {nombre} · {Path.GetFileName(_proyecto.Archivo)}";
        }
    }

    // ---- Persistencia .lpx.json + proyectos recientes (commit 32) -------

    /// <summary>
    /// Colección de proyectos recientes leídos de <see cref="ProyectoRegistry"/>.
    /// Compartida con MemoriaPlus.App: ambos clientes leen/escriben el mismo
    /// JSON en <c>%APPDATA%/MemoriaPlus/recents.json</c>, así un proyecto
    /// abierto en una app aparece en la otra.
    /// </summary>
    public ObservableCollection<MemoriaPlusVm.ProyectoResumen> ProyectosRecientes { get; } = new();

    private MemoriaPlusVm.ProyectoResumen? _proyectoRecienteSeleccionado;
    public MemoriaPlusVm.ProyectoResumen? ProyectoRecienteSeleccionado
    {
        get => _proyectoRecienteSeleccionado;
        set
        {
            _proyectoRecienteSeleccionado = value;
            OnPropertyChanged();
            (AbrirEnEditorCommand as RelayCommand)?.Execute(null);  // no-op si null
        }
    }

    private string _statusPersistencia = "";
    /// <summary>Mensaje breve del último resultado de persistencia .lpx.json.</summary>
    public string StatusPersistencia
    {
        get => _statusPersistencia;
        private set { _statusPersistencia = value; OnPropertyChanged(); }
    }

    public ICommand? NuevoProyectoLpxCommand   { get; private set; }
    public ICommand? AbrirProyectoLpxCommand   { get; private set; }
    public ICommand? GuardarProyectoLpxCommand { get; private set; }
    public ICommand? GuardarComoLpxCommand     { get; private set; }
    public ICommand? AbrirProyectoRecienteCommand { get; private set; }
    public ICommand? AbrirEnEditorCommand      { get; private set; }
    public ICommand? IrAExploradorCommand      { get; private set; }
    public ICommand? UndoCommand               { get; private set; }
    public ICommand? RedoCommand               { get; private set; }
    public ICommand? AbrirShortcutsCommand     { get; private set; }
    public ICommand? AplicarBulkCommand        { get; private set; }

    // ---- Validación normativa (commit 33) ----
    /// <summary>
    /// Sub-VM compartido con MemoriaPlus.App vía src.UI.Shared. Consume el
    /// ValidationEngine de Core (4 reglas R-001 / ACI 318) y mantiene un
    /// reporte vivo bound al chip indicador del toolbar y al panel lateral.
    /// </summary>
    public MemoriaPlusVm.ValidacionViewModel Validacion { get; } = new();

    // ---- Búsqueda global (commit 35) ----
    public MemoriaPlusVm.BusquedaViewModel Busqueda { get; private set; } = null!;

    /// <summary>
    /// Sub-ViewModel del modo Plano CAD (Fase 1.B del PLAN_CAD_V1). Coordina
    /// la importación de planos .DXF; las losas que dibuja vienen del SSOT
    /// (<see cref="Sistema"/>.Losas), no de un estado propio.
    /// </summary>
    public CadEditorViewModel CadEditor { get; private set; } = null!;

    /// <summary>
    /// Sub-VM de la pestaña «Cargas y Combinaciones» (Fase 2): gestiona los
    /// casos y combinaciones de carga del proyecto.
    /// </summary>
    public CargasCombinacionesViewModel CargasCombinaciones { get; private set; } = null!;

    /// <summary>
    /// Sub-VM del editor de vigas continuas (Fase 3): gestiona la viga activa,
    /// su edición y el renderizado de diagramas analíticos con OxyPlot.
    /// </summary>
    public VigaEditorViewModel VigaEditor { get; private set; } = null!;

    /// <summary>VM de la vista «Bajada de cargas» (Fase J): transmisión vertical + predim de zapata.</summary>
    public BajadaCargasViewModel BajadaCargas { get; private set; } = null!;

    /// <summary>VM del editor de columnas (Fase J): CRUD de las columnas del edificio activo.</summary>
    public ColumnasEditorViewModel ColumnasEditor { get; private set; } = null!;

    public ICommand? IrABusquedaCommand { get; private set; }
    public ICommand? GenerarMemoriaCommand { get; private set; }
    public ICommand? AutoBalanceoCommand { get; private set; }

    private bool _modoConectarBordes;
    /// <summary>
    /// Toggle de "modo conexión por ID": cuando es true, click en una celda
    /// ID del LosasGrid registra el primer endpoint; el siguiente click
    /// crea el borde adicional (X o Y según la heurística del LayoutSolver).
    /// </summary>
    public bool ModoConectarBordes
    {
        get => _modoConectarBordes;
        set
        {
            if (_modoConectarBordes == value) return;
            _modoConectarBordes = value;
            OnPropertyChanged();
            _primerIdParaBorde = null;  // reset al togglear
        }
    }

    private int? _primerIdParaBorde;
    /// <summary>
    /// Primera losa clickeada en modo conexión. Null cuando esperamos el
    /// primer click. El segundo click (sobre una losa distinta) crea el
    /// BordeAdic y resetea el state.
    /// </summary>
    public int? PrimerIdParaBorde => _primerIdParaBorde;

    /// <summary>
    /// Procesa un click en la celda ID de una losa cuando ModoConectarBordes
    /// está activo. Si es el primer click, lo recuerda; si es el segundo
    /// (sobre distinta losa), crea un BordeAdic en BordesX si las losas son
    /// adyacentes horizontalmente, BordesY si verticalmente, o BordesX por
    /// default si la heurística no decide (el usuario puede mover después).
    /// </summary>
    public void HandleIdClickParaBorde(int losaId)
    {
        if (!ModoConectarBordes) return;
        if (_primerIdParaBorde is null)
        {
            _primerIdParaBorde = losaId;
            OnPropertyChanged(nameof(PrimerIdParaBorde));
            Log($"Conectar: primera losa = #{losaId}. Click la segunda losa adyacente.");
            return;
        }
        var primer = _primerIdParaBorde.Value;
        if (primer == losaId)
        {
            Log("Conectar: misma losa — cancelado.");
            _primerIdParaBorde = null;
            OnPropertyChanged(nameof(PrimerIdParaBorde));
            return;
        }

        PushUndoSnapshot();
        // Heurística de eje: si BI<BJ por ID, los agregamos a BordesX (convención
        // simple). El usuario puede después editar el grid si está mal.
        var bi = Math.Min(primer, losaId);
        var bj = Math.Max(primer, losaId);
        var nuevo = new BordeAdic { BI = bi, BJ = bj, Balanceo = "S" };

        // Determinar el balanceo correcto según tipos de las losas.
        if (LosaTieneVoladizo(bi) || LosaTieneVoladizo(bj))
            nuevo.Balanceo = "N";

        SistemaActivo.BordesX.Add(nuevo);
        Log($"Borde X creado: I={bi} J={bj} BAL={nuevo.Balanceo}.");
        _primerIdParaBorde = null;
        ModoConectarBordes = false;  // sale del modo después de un par exitoso
        OnPropertyChanged(nameof(PrimerIdParaBorde));
        RefreshDLContent();
    }

    private bool LosaTieneVoladizo(int losaId)
    {
        var losa = SistemaActivo.Losas.FirstOrDefault(l => l.Id == losaId);
        if (losa is null) return false;
        return TipoLosa.Catalogo.TryGetValue(losa.Tipo, out var t) && t.BordesVuelo > 0;
    }

    /// <summary>
    /// Recorre todos los bordes (X e Y) y setea Balanceo="N" donde al menos
    /// una de las dos losas tiene BordesVuelo > 0 (i.e. su tipo Pieper-Martens
    /// incluye un voladizo). Refleja la convención del motor original de
    /// F. Perdomo en Losas.exe: voladizo ⇒ no aplicar balanceo de momentos.
    /// </summary>
    public void AplicarAutoBalanceo()
    {
        if (SistemaActivo is null) return;
        PushUndoSnapshot();
        int cambiados = 0;
        foreach (var b in SistemaActivo.BordesX.Concat(SistemaActivo.BordesY))
        {
            var debiera = (LosaTieneVoladizo(b.BI) || LosaTieneVoladizo(b.BJ)) ? "N" : "S";
            if (b.Balanceo != debiera)
            {
                b.Balanceo = debiera;
                cambiados++;
            }
        }
        Log($"Auto-balanceo: {cambiados} bordes ajustados (voladizo ⇒ N).");
        RefreshDLContent();
    }

    private string _statusGeneracion = "";
    /// <summary>Mensaje del último intento de generación de memoria .docx.</summary>
    public string StatusGeneracion
    {
        get => _statusGeneracion;
        private set { _statusGeneracion = value; OnPropertyChanged(); }
    }

    // ---- Auto-backup (configurable) ----

    private bool _autoBackupActivo = true;
    /// <summary>
    /// Si <c>true</c>, cada Guardar produce además un duplicado timestamped
    /// en el subfolder <c>backups/</c> junto al .lpx.json (no reemplaza el
    /// archivo principal, solo agrega). Se mantienen las últimas
    /// <see cref="MaxBackups"/> copias por proyecto.
    /// </summary>
    public bool AutoBackupActivo
    {
        get => _autoBackupActivo;
        set { _autoBackupActivo = value; OnPropertyChanged(); }
    }

    private const int MaxBackups = 20;

    /// <summary>
    /// Hace una copia timestamped del proyecto activo en
    /// <c>{carpeta}/backups/{nombre}_{yyyyMMdd-HHmmss}.lpx.json</c>. No-op
    /// si el proyecto no tiene path (no guardado todavía) o si
    /// <see cref="AutoBackupActivo"/> es false. Best-effort: errores se
    /// loggean pero no bloquean el Save principal.
    /// </summary>
    private void MaybeBackup()
    {
        if (!_autoBackupActivo) return;
        if (string.IsNullOrEmpty(_proyecto.Archivo)) return;
        if (!File.Exists(_proyecto.Archivo)) return;
        try
        {
            var dir = Path.GetDirectoryName(_proyecto.Archivo);
            if (string.IsNullOrEmpty(dir)) return;
            var backupsDir = Path.Combine(dir, "backups");
            Directory.CreateDirectory(backupsDir);
            var nombre = Path.GetFileNameWithoutExtension(_proyecto.Archivo);
            // .lpx.json es una doble-extensión; conservar el "stem" base.
            if (nombre.EndsWith(".lpx", StringComparison.OrdinalIgnoreCase))
                nombre = nombre[..^4];
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(backupsDir, $"{nombre}_{stamp}.lpx.json");
            File.Copy(_proyecto.Archivo, backupPath, overwrite: false);

            // Prune: mantener solo las últimas MaxBackups copias.
            var copias = Directory.GetFiles(backupsDir, $"{nombre}_*.lpx.json")
                                  .OrderByDescending(f => f)
                                  .Skip(MaxBackups)
                                  .ToList();
            foreach (var old in copias)
                try { File.Delete(old); } catch { /* ignorar */ }

            Log($"Backup automático: {Path.GetFileName(backupPath)} ({copias.Count} viejas eliminadas).");
        }
        catch (Exception ex)
        {
            Log("Auto-backup falló (no bloqueante): " + ex.Message);
        }
    }

    // ---- Undo/Redo infraestructura (snapshots de ProyectoSerializer) ----

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private const int MaxUndoLevels = 50;
    private bool _restoringSnapshot;

    public bool PuedeUndo => _undoStack.Count > 0 && !_restoringSnapshot;
    public bool PuedeRedo => _redoStack.Count > 0 && !_restoringSnapshot;

    /// <summary>
    /// Toma un snapshot del proyecto actual y lo apila como undo. Llamado
    /// ANTES de cualquier mutación significativa (agregar losa, eliminar,
    /// aplicar tipo, edit commit, bulk apply, etc.). Limita la pila a
    /// <see cref="MaxUndoLevels"/> entries (drop el más viejo).
    /// </summary>
    public void PushUndoSnapshot()
    {
        if (_restoringSnapshot) return;  // no auto-record durante restore
        try
        {
            var snapshot = ProyectoSerializer.ToJson(_proyecto);
            _undoStack.Push(snapshot);
            while (_undoStack.Count > MaxUndoLevels)
            {
                // Drop el más viejo: el Stack<T> no soporta RemoveAt, así que
                // copiamos al revés sin el último.
                var keep = _undoStack.ToArray().Take(MaxUndoLevels).Reverse().ToArray();
                _undoStack.Clear();
                foreach (var s in keep) _undoStack.Push(s);
            }
            _redoStack.Clear();
            OnPropertyChanged(nameof(PuedeUndo));
            OnPropertyChanged(nameof(PuedeRedo));
        }
        catch { /* snapshot best-effort */ }
    }

    private void Undo()
    {
        if (!PuedeUndo) return;
        var current = ProyectoSerializer.ToJson(_proyecto);
        var previous = _undoStack.Pop();
        _redoStack.Push(current);
        RestoreSnapshot(previous);
        OnPropertyChanged(nameof(PuedeUndo));
        OnPropertyChanged(nameof(PuedeRedo));
        Log("Undo aplicado.");
    }

    private void Redo()
    {
        if (!PuedeRedo) return;
        var current = ProyectoSerializer.ToJson(_proyecto);
        var next = _redoStack.Pop();
        _undoStack.Push(current);
        RestoreSnapshot(next);
        OnPropertyChanged(nameof(PuedeUndo));
        OnPropertyChanged(nameof(PuedeRedo));
        Log("Redo aplicado.");
    }

    private void RestoreSnapshot(string json)
    {
        try
        {
            _restoringSnapshot = true;
            var restored = ProyectoSerializer.FromJson(json);
            _proyecto.Sistemas.Clear();
            foreach (var s in restored.Sistemas) _proyecto.Sistemas.Add(s);
            _proyecto.Archivo     = restored.Archivo;
            _proyecto.Nombre      = restored.Nombre;
            _proyecto.Autor       = restored.Autor;
            _proyecto.CodigoObra  = restored.CodigoObra;
            _proyecto.Ubicacion   = restored.Ubicacion;
            _proyecto.Descripcion = restored.Descripcion;

            // Restaurar la base de cargas (Fase 2) sin reasignar el objeto: se
            // mantienen estables sus ObservableCollection bindeadas por la
            // pestaña «Cargas y Combinaciones» — patrón idéntico al de Sistemas.
            _proyecto.Combinaciones.Norma = restored.Combinaciones.Norma;
            _proyecto.Combinaciones.Casos.Clear();
            foreach (var c in restored.Combinaciones.Casos)
                _proyecto.Combinaciones.Casos.Add(c);
            _proyecto.Combinaciones.Combinaciones.Clear();
            foreach (var c in restored.Combinaciones.Combinaciones)
                _proyecto.Combinaciones.Combinaciones.Add(c);
            CargasCombinaciones.NotificarRestauracion();

            // Restaurar las vigas del nivel por defecto (Fase 3) — Clear/re-add
            // sobre la ObservableCollection estable, mismo patrón que Sistemas.
            _proyecto.AsegurarEstructura();
            var nivelRestaurado = _proyecto.Edificios[0].Niveles[0];
            nivelRestaurado.Vigas.Clear();
            foreach (var v in restored.Edificios[0].Niveles[0].Vigas)
                nivelRestaurado.Vigas.Add(v);
            VigaEditor.NotificarRestauracion();

            SistemaActivo = _proyecto.Sistemas.FirstOrDefault() ?? NuevoSistemaDemo();
            OnPropertyChanged(nameof(Proyecto));
            OnPropertyChanged(nameof(TituloVentana));
            RefreshDLContent();
        }
        finally { _restoringSnapshot = false; }
    }

    // ---- Multi-select + bulk apply ----

    /// <summary>
    /// Losas seleccionadas en el DataGrid del Editor (multi-select). Se
    /// actualiza desde code-behind via SelectionChanged.
    /// </summary>
    public ObservableCollection<Losa> LosasSeleccionadas { get; } = new();

    private int _bulkSeleccionadasCount;
    public int BulkSeleccionadasCount
    {
        get => _bulkSeleccionadasCount;
        private set { _bulkSeleccionadasCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(MostrarBulkPanel)); }
    }

    public bool MostrarBulkPanel => _bulkSeleccionadasCount >= 2;

    // Valores del bulk-apply (strings para permitir "" = no aplicar este campo).
    private string _bulkLx = "", _bulkLy = "", _bulkEspesor = "", _bulkCarga = "", _bulkTipo = "";
    public string BulkLx      { get => _bulkLx;      set { _bulkLx = value; OnPropertyChanged(); } }
    public string BulkLy      { get => _bulkLy;      set { _bulkLy = value; OnPropertyChanged(); } }
    public string BulkEspesor { get => _bulkEspesor; set { _bulkEspesor = value; OnPropertyChanged(); } }
    public string BulkCarga   { get => _bulkCarga;   set { _bulkCarga = value; OnPropertyChanged(); } }
    public string BulkTipo    { get => _bulkTipo;    set { _bulkTipo = value; OnPropertyChanged(); } }

    /// <summary>Llamado desde el code-behind al cambiar SelectedItems del DataGrid.</summary>
    public void ActualizarLosasSeleccionadas(System.Collections.IList selectedItems)
    {
        LosasSeleccionadas.Clear();
        foreach (var item in selectedItems)
            if (item is Losa l) LosasSeleccionadas.Add(l);
        BulkSeleccionadasCount = LosasSeleccionadas.Count;
    }

    // Nota: el panel de refuerzo comercial fue movido a la pestaña sidebar
    // "Aceros" como "Próximamente". El modelo Core (RefuerzoBarras, AreasBarras,
    // Losa.RefuerzoX/Y) sigue siendo persistido y exportado al CSV/XLSX. La
    // reactividad de la UI (suscripción + recalc por cambio) se reactivará
    // cuando se implemente el panel definitivo en la pestaña Aceros.

    /// <summary>
    /// Callback inyectado por MainWindow para abrir
    /// <c>KeyboardShortcutsWindow</c> (que vive en Views/). Mantengo el VM
    /// ignorante de la window concreta para que sea testable.
    /// </summary>
    public Action? OnAbrirShortcuts { get; set; }

    private void AbrirShortcutsModal() => OnAbrirShortcuts?.Invoke();

    private void AplicarBulk()
    {
        if (LosasSeleccionadas.Count < 2) return;
        PushUndoSnapshot();

        int aplicados = 0;
        bool aplicarLx      = double.TryParse(_bulkLx,      out var lx);
        bool aplicarLy      = double.TryParse(_bulkLy,      out var ly);
        bool aplicarEspesor = double.TryParse(_bulkEspesor, out var esp);
        bool aplicarCarga   = double.TryParse(_bulkCarga,   out var car);
        bool aplicarTipo    = int.TryParse(_bulkTipo,       out var tipo);

        foreach (var l in LosasSeleccionadas)
        {
            if (aplicarLx)      l.Lx      = lx;
            if (aplicarLy)      l.Ly      = ly;
            if (aplicarEspesor) l.Espesor = esp;
            if (aplicarCarga)   l.Carga   = car;
            if (aplicarTipo && TipoLosa.EsCodigoValido(tipo)) l.Tipo = tipo;
            aplicados++;
        }
        // Reset campos para que la próxima vez no se queden con valores viejos.
        BulkLx = BulkLy = BulkEspesor = BulkCarga = BulkTipo = "";
        Log($"Bulk apply: {aplicados} losas modificadas.");
        RefreshDLContent();
    }

    public Proyecto Proyecto => _proyecto;

    /// <summary>
    /// Sistema actualmente activo (el que se edita en el Editor / Esquema / etc.).
    /// Cambiar este valor refresca todas las vistas suscritas vía NotifyPropertyChanged.
    /// </summary>
    public Sistema SistemaActivo
    {
        get => _sistemaActivo;
        set
        {
            if (ReferenceEquals(_sistemaActivo, value)) return;
            _sistemaActivo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Sistema));
            OnPropertyChanged(nameof(LosasFiltradas));
            RefreshDLContent();
        }
    }

    /// <summary>
    /// Edificio activo del proyecto (el primero), fuente del modelo para la
    /// Vista 3D (Fase I). Nulo si el proyecto aún no tiene edificios.
    /// </summary>
    public Edificio? EdificioActivo => _proyecto.Edificios.FirstOrDefault();

    /// <summary>Alias retro-compatible: el resto del código y los XAML siguen accediendo a "Sistema".</summary>
    public Sistema Sistema
    {
        get => _sistemaActivo;
        set
        {
            SistemaActivo = value;
            // Si cambió la referencia, asegurar que esté en el proyecto.
            if (!_proyecto.Sistemas.Contains(value))
            {
                _proyecto.Sistemas.Clear();
                _proyecto.Sistemas.Add(value);
            }
        }
    }

    public string LosasExePath
    {
        get => _losasExePath;
        set { _losasExePath = value; OnPropertyChanged(); }
    }

    public string? DLPath
    {
        get => _dlPath;
        set { _dlPath = value; OnPropertyChanged(); }
    }

    public string? TxtPath
    {
        get => _txtPath;
        set { _txtPath = value; OnPropertyChanged(); }
    }

    public string TxtContent
    {
        get => _txtContent;
        set { _txtContent = value; OnPropertyChanged(); }
    }

    public string DLContent
    {
        get => _dlContent;
        set { _dlContent = value; OnPropertyChanged(); }
    }

    public string LogText
    {
        get => _logText;
        set { _logText = value; OnPropertyChanged(); }
    }

    public string FiltroTipo
    {
        get => _filtroTipo;
        set { _filtroTipo = value; OnPropertyChanged(); OnPropertyChanged(nameof(LosasFiltradas)); }
    }

    public bool Ocupado
    {
        get => _ocupado;
        set { _ocupado = value; OnPropertyChanged(); }
    }

    /// <summary>Vista filtrada para la tabla principal (búsqueda por tipo).</summary>
    public ObservableCollection<Losa> LosasFiltradas
    {
        get
        {
            var src = Sistema.Losas.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(_filtroTipo))
            {
                if (int.TryParse(_filtroTipo, out var t))
                    src = src.Where(l => l.Tipo == t);
                else
                    src = src.Where(l => l.TipoDescripcion.Contains(_filtroTipo, StringComparison.OrdinalIgnoreCase));
            }
            return new ObservableCollection<Losa>(src);
        }
    }

    public ObservableCollection<TipoLosa> TiposCatalogo { get; } =
        new ObservableCollection<TipoLosa>(TipoLosa.Catalogo.Values);

    public PluginHost Plugins { get; }

    public MainViewModel()
    {
        // El proyecto arranca con el sistema demo activo.
        _proyecto.Sistemas.Add(_sistemaActivo);

        // ---- Commands de persistencia .lpx.json (commit 32) ----
        NuevoProyectoLpxCommand   = new RelayCommand(_ => NuevoProyectoLpx());
        AbrirProyectoLpxCommand   = new RelayCommand(_ => AbrirProyectoLpxDialog());
        GuardarProyectoLpxCommand = new RelayCommand(_ => GuardarProyectoLpx());
        GuardarComoLpxCommand     = new RelayCommand(_ => GuardarComoLpx());
        AbrirProyectoRecienteCommand = new RelayCommand(p =>
        {
            if (p is string path) AbrirProyectoLpxPorPath(path);
        });
        AbrirEnEditorCommand = new RelayCommand(_ =>
        {
            var sel = _proyectoRecienteSeleccionado;
            if (sel is null || string.IsNullOrEmpty(sel.Path)) return;
            AbrirProyectoLpxPorPath(sel.Path);
            ModoActivo = ModoSidebar.Editor;
        }, _ => _proyectoRecienteSeleccionado is not null
                && !string.IsNullOrEmpty(_proyectoRecienteSeleccionado.Path));
        IrAExploradorCommand = new RelayCommand(_ => ModoActivo = ModoSidebar.Explorador);
        UndoCommand          = new RelayCommand(_ => Undo(), _ => PuedeUndo);
        RedoCommand          = new RelayCommand(_ => Redo(), _ => PuedeRedo);
        AbrirShortcutsCommand = new RelayCommand(_ => AbrirShortcutsModal());
        AplicarBulkCommand   = new RelayCommand(_ => AplicarBulk(), _ => MostrarBulkPanel);

        // ---- Búsqueda global (commit 35) ----
        Busqueda = new MemoriaPlusVm.BusquedaViewModel(
            getProyectosRecientes: () => ProyectosRecientes,
            getProyectoActivo:     () => _proyecto,
            abrirProyectoPorPath:  path => { AbrirProyectoLpxPorPath(path); ModoActivo = ModoSidebar.Editor; },
            irASistema:            BuscarYActivarSistema,
            irALosa:               (s, id) => BuscarYActivarSistema(s));
        IrABusquedaCommand = new RelayCommand(_ => ModoActivo = ModoSidebar.Busqueda);
        GenerarMemoriaCommand = new RelayCommand(_ => GenerarMemoria());
        AutoBalanceoCommand   = new RelayCommand(_ => AplicarAutoBalanceo());

        // ---- Plano CAD (Fase 1.B/2) — el sub-VM lee las losas del sistema
        // activo y, al mapear un polígono, toma snapshot de undo antes de mutar.
        CadEditor = new CadEditorViewModel(
            getSistemaActivo: () => _sistemaActivo,
            pushUndoSnapshot: PushUndoSnapshot);

        // ---- Cargas y Combinaciones (Fase 2) ----
        CargasCombinaciones = new CargasCombinacionesViewModel(_proyecto, PushUndoSnapshot);

        // ---- Editor de vigas continuas (Fase 3) ----
        VigaEditor = new VigaEditorViewModel(_proyecto, PushUndoSnapshot);
        BajadaCargas = new BajadaCargasViewModel(() => EdificioActivo);
        ColumnasEditor = new ColumnasEditorViewModel(() => EdificioActivo);

        // Cambios al nombre del proyecto refrescan el título de la ventana.
        _proyecto.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Proyecto.Nombre) || e.PropertyName == nameof(Proyecto.Archivo))
                OnPropertyChanged(nameof(TituloVentana));
        };

        // ---- Validación normativa (commit 33) ----
        // Primera validación + re-validación en cada cambio del modelo.
        Validacion.RevalidarPara(_proyecto);
        _proyecto.Sistemas.CollectionChanged += (_, _) => Validacion.Revalidar();
        SuscribirRevalidacionEnSistemas();

        // Cargar lista de proyectos recientes al arrancar.
        RecargarProyectosRecientes();

        // AppContext.BaseDirectory funciona también en single-file publish (donde
        // Assembly.Location devuelve string vacío y rompía el constructor).
        var dir = AppContext.BaseDirectory;

        // Buscar carpeta /plugins: 1) junto al ejecutable, 2) raíz del proyecto en dev (.../LosasPlus/plugins)
        string? pluginsDir = Path.Combine(dir, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            // intentar subiendo hasta encontrar 'plugins/' (modo desarrollo)
            var probe = new DirectoryInfo(dir);
            while (probe != null)
            {
                var cand = Path.Combine(probe.FullName, "plugins");
                if (Directory.Exists(cand)) { pluginsDir = cand; break; }
                probe = probe.Parent;
            }
        }
        Plugins = new PluginHost(pluginsDir ?? Path.Combine(dir, "plugins"));

        // Localizar Losas.exe: 1) junto al exe, 2) carpeta padre, 3) abuela
        string? losas = null;
        var probe2 = new DirectoryInfo(dir);
        for (int i = 0; i < 3 && probe2 != null; i++)
        {
            var cand = Path.Combine(probe2.FullName, "Losas.exe");
            if (File.Exists(cand)) { losas = cand; break; }
            probe2 = probe2.Parent;
        }
        _losasExePath = losas ?? "";

        RefreshDLContent();
    }

    public void Log(string msg)
    {
        LogText = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n" + LogText;
    }

    public void RefreshDLContent()
    {
        try { DLContent = DLFileService.WriteAll(_proyecto.Sistemas); }
        catch (Exception ex) { Log("Error al serializar .DL: " + ex.Message); }
    }

    public void AbrirDL(string path)
    {
        try
        {
            var sistemas = DLFileService.ReadAll(path);
            if (sistemas.Count == 0) { Log("El .DL no contiene ningún sistema."); return; }

            _proyecto.Sistemas.Clear();
            foreach (var s in sistemas) _proyecto.Sistemas.Add(s);
            _proyecto.Archivo = path;
            _proyecto.Nombre = Path.GetFileNameWithoutExtension(path);

            SistemaActivo = sistemas[0];
            DLPath = path;
            Log($"Cargado .DL: {path} ({sistemas.Count} sistema{(sistemas.Count == 1 ? "" : "s")})");
            OnPropertyChanged(nameof(LosasFiltradas));
            OnPropertyChanged(nameof(Proyecto));

            // Sin esto el archivo se carga pero el usuario no lo ve si estaba en
            // Explorador / Acerca / Salida — el grid de losas vive en Editor.
            // Reportado como "el botón abrir .DL parece que no abre".
            ModoActivo = ModoSidebar.Editor;
        }
        catch (Exception ex) { Log("Error abriendo .DL: " + ex.Message); }
    }

    /// <summary>Abre un proyecto multi-archivo (manifest <c>proyecto.lpx.json</c>).</summary>
    public void AbrirProyecto(string manifestPath)
    {
        try
        {
            var p = ProyectoService.AbrirProyecto(manifestPath);
            _proyecto.Sistemas.Clear();
            foreach (var s in p.Sistemas) _proyecto.Sistemas.Add(s);
            _proyecto.Archivo = p.Archivo;
            _proyecto.Nombre = p.Nombre;
            _proyecto.Autor = p.Autor;
            _proyecto.CodigoObra = p.CodigoObra;
            _proyecto.Ubicacion = p.Ubicacion;
            _proyecto.Descripcion = p.Descripcion;
            _proyecto.FechaCreacion = p.FechaCreacion;

            SistemaActivo = _proyecto.Sistemas.First();
            Log($"Proyecto abierto: '{p.Nombre}' ({p.Sistemas.Count} sistemas) desde {manifestPath}");
            OnPropertyChanged(nameof(Proyecto));
            OnPropertyChanged(nameof(LosasFiltradas));
            RefreshDLContent();

            // Mismo motivo que AbrirDL: si el usuario abrió desde Explorador,
            // nunca verá el contenido si no saltamos al Editor.
            ModoActivo = ModoSidebar.Editor;
        }
        catch (Exception ex) { Log("Error abriendo proyecto: " + ex.Message); }
    }

    /// <summary>Guarda el proyecto como multi-archivo en una carpeta destino.</summary>
    public void GuardarProyecto(string carpetaDestino)
    {
        try
        {
            ProyectoService.GuardarProyecto(_proyecto, carpetaDestino);
            DLPath = _proyecto.Archivo;
            Log($"Proyecto guardado en {carpetaDestino} con {_proyecto.Sistemas.Count} sistema(s) — manifest: {ProyectoService.ManifestFileName}");
            OnPropertyChanged(nameof(Proyecto));
        }
        catch (Exception ex) { Log("Error guardando proyecto: " + ex.Message); }
    }

    /// <summary>Agrega un sistema vacío al proyecto y lo deja activo.</summary>
    public Sistema AgregarSistema(string? nombre = null)
    {
        var n = _proyecto.Sistemas.Count + 1;
        var nuevo = new Sistema
        {
            Nombre = nombre ?? $"Sistema No {n}",
            Fc = SistemaActivo.Fc,
            Fy = SistemaActivo.Fy,
            Adicionales = SistemaActivo.Adicionales,
        };
        _proyecto.Sistemas.Add(nuevo);
        SistemaActivo = nuevo;
        Log($"Sistema agregado: {nuevo.Nombre}");
        return nuevo;
    }

    /// <summary>Elimina el sistema dado del proyecto. Si era el activo, queda activo el anterior.</summary>
    public void EliminarSistema(Sistema s)
    {
        if (_proyecto.Sistemas.Count <= 1)
        {
            Log("No se puede eliminar: el proyecto debe tener al menos un sistema.");
            return;
        }
        var idx = _proyecto.Sistemas.IndexOf(s);
        if (idx < 0) return;
        var fueActivo = ReferenceEquals(_sistemaActivo, s);
        _proyecto.Sistemas.Remove(s);
        if (fueActivo)
            SistemaActivo = _proyecto.Sistemas[Math.Min(idx, _proyecto.Sistemas.Count - 1)];
        Log($"Sistema eliminado: {s.Nombre}");
    }

    public async Task GuardarDLAsync(string path)
    {
        try
        {
            await Plugins.LoadAllAsync(Log);
            await Plugins.RunHookAsync("pre-dl", BuildPluginContext(), Log);

            // Guarda TODOS los sistemas del proyecto (uno o varios)
            DLFileService.SaveAll(_proyecto.Sistemas, path);
            DLPath = path;
            _proyecto.Archivo = path;
            RefreshDLContent();
            var n = _proyecto.Sistemas.Count;
            Log($"Guardado .DL: {path} ({n} sistema{(n == 1 ? "" : "s")})");
        }
        catch (Exception ex) { Log("Error guardando .DL: " + ex.Message); }
    }

    /// <summary>
    /// Lanza el motor de cálculo original Losas.exe (de F. Perdomo) sin automatizarlo:
    /// el binario es de Visual Smalltalk Enterprise 3.1, ignora argumentos CLI y no expone
    /// patterns UIA programables (ver <c>docs/RUNNER_BEHAVIOR.md</c>). El usuario carga el
    /// .DL, ejecuta y guarda el .TXT manualmente desde la GUI nativa, luego usa
    /// "Importar .TXT" en LosasPlus para traer los resultados.
    /// </summary>
    public async Task LanzarLosasExeAsync()
    {
        if (string.IsNullOrWhiteSpace(LosasExePath) || !File.Exists(LosasExePath))
        { Log("Configura la ruta de Losas.exe en el panel superior."); return; }

        try
        {
            await Plugins.LoadAllAsync(Log);
            await Plugins.RunHookAsync("pre-run", BuildPluginContext(), Log);

            var psi = new ProcessStartInfo
            {
                FileName = LosasExePath,
                WorkingDirectory = Path.GetDirectoryName(LosasExePath) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            };
            var p = Process.Start(psi);
            Log(p != null
                ? $"Losas.exe lanzado (PID {p.Id}). Cargá el .DL desde el menú File del programa, ejecutá el cálculo y guardá el .TXT."
                : "No se pudo lanzar Losas.exe.");
        }
        catch (Exception ex) { Log("Error lanzando Losas.exe: " + ex.Message); }
    }

    private PluginContext BuildPluginContext(string? outputTxt = null, string? xlsx = null, string? csv = null)
        => new PluginContext
        {
            Sistema = Sistema,
            OutputTxt = outputTxt,
            OutputXlsxPath = xlsx,
            OutputCsvPath = csv,
            PluginsDir = Plugins.PluginsDirectory,
            DLPath = DLPath,
            LosasExePath = LosasExePath,
            HostLog = Log,
        };

    /// <summary>
    /// Carga un .TXT producido manualmente por Losas.exe, lo parsea, lo asocia a las losas
    /// del sistema actual y dispara el hook 'post-txt'.
    /// </summary>
    public async Task ImportarTxtAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        { Log("Archivo .TXT no encontrado: " + path); return; }

        try
        {
            Ocupado = true;
            var content = await File.ReadAllTextAsync(path, Encoding.GetEncoding(1252));
            TxtPath = path;
            TxtContent = content;

            var parsed = TxtParser.Parse(content, Sistema.Losas);
            TxtParser.Apply(parsed, Sistema.Losas);
            TxtParser.ApplyApoyos(parsed, Sistema.BordesX, Sistema.BordesY);
            OnPropertyChanged(nameof(LosasFiltradas));

            await Plugins.LoadAllAsync(Log);
            var ctx = BuildPluginContext(outputTxt: content);
            await Plugins.RunHookAsync("post-txt", ctx, Log);
            // 'post-run' es alias de post-txt: el .TXT representa el output de la corrida
            // del motor, así que ambos hooks tienen la misma semántica práctica.
            await Plugins.RunHookAsync("post-run", ctx, Log);

            Log($"Importado .TXT: {path} ({parsed.PorLosa.Count} losas con resultados detectados)");
        }
        catch (Exception ex) { Log("Error importando .TXT: " + ex.Message); }
        finally { Ocupado = false; }
    }

    public void AgregarLosa()
    {
        PushUndoSnapshot();
        var nuevoId = Sistema.Losas.Count == 0 ? 1 : Sistema.Losas.Max(l => l.Id) + 1;
        Sistema.Losas.Add(new Losa { Id = nuevoId, Tipo = 11, Carga = 2.0, Espesor = 0.12, Lx = 4, Ly = 4, Rec = 0.02 });
        OnPropertyChanged(nameof(LosasFiltradas));
        RefreshDLContent();
    }

    public void EliminarLosa(Losa l)
    {
        PushUndoSnapshot();
        Sistema.Losas.Remove(l);
        OnPropertyChanged(nameof(LosasFiltradas));
        RefreshDLContent();
    }

    public void AgregarBorde(bool eje_X)
    {
        PushUndoSnapshot();
        var coll = eje_X ? Sistema.BordesX : Sistema.BordesY;
        coll.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        RefreshDLContent();
    }

    public void EliminarBorde(BordeAdic b)
    {
        PushUndoSnapshot();
        if (Sistema.BordesX.Remove(b)) { RefreshDLContent(); return; }
        if (Sistema.BordesY.Remove(b)) RefreshDLContent();
    }

    public async Task ExportarCsvAsync(string path)
    {
        try
        {
            // Refrescar outputs del motor antes de exportar para que las columnas
            // αfm, Heq, Qd, Ql, Qu, As, V_bovedilla/concreto no queden desactualizadas
            // cuando el usuario edita y exporta sin pasar por F5.
            try { LosasPlus.Calculo.CalculoEngine.RecalcularSistema(Sistema, _proyecto); }
            catch (Exception recalcEx) { Log("Aviso recalc previo a CSV: " + recalcEx.Message); }

            CsvExporter.Export(Sistema, path);
            Log("CSV exportado: " + path);
            await Plugins.LoadAllAsync(Log);
            await Plugins.RunHookAsync("custom-export", BuildPluginContext(csv: path), Log);
        }
        catch (Exception ex) { Log("Error exportando CSV: " + ex.Message); }
    }

    /// <summary>
    /// Exporta a XLSX con varias hojas (Resumen / Losas / Apoyos / Esquema / Combinaciones).
    /// La ventana llama a este método después de capturar el PNG del Canvas (opcional).
    /// </summary>
    public async Task ExportarXlsxAsync(string path, byte[]? esquemaPng = null)
    {
        try
        {
            // Refrescar outputs del motor antes de exportar (mismo motivo que CSV).
            try { LosasPlus.Calculo.CalculoEngine.RecalcularSistema(Sistema, _proyecto); }
            catch (Exception recalcEx) { Log("Aviso recalc previo a XLSX: " + recalcEx.Message); }

            string? dzp = null, cez = null;
            if (!string.IsNullOrEmpty(LosasExePath))
            {
                var dir = Path.GetDirectoryName(LosasExePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var candDzp = Path.Combine(dir, "Combinaciones.DZP");
                    var candCez = Path.Combine(dir, "Combinaciones.CEZ");
                    if (File.Exists(candDzp)) dzp = candDzp;
                    if (File.Exists(candCez)) cez = candCez;
                }
            }
            XlsxExporter.Export(new XlsxExporter.ExportContext
            {
                Sistema = Sistema,
                EsquemaPng = esquemaPng,
                CombinacionesDzpPath = dzp,
                CombinacionesCezPath = cez,
                TxtSalidaPath = TxtPath,
                TxtSalidaContenido = TxtContent,
                DLPath = DLPath,
            }, path);
            Log("XLSX exportado: " + path);

            await Plugins.LoadAllAsync(Log);
            await Plugins.RunHookAsync("custom-export", BuildPluginContext(xlsx: path), Log);
        }
        catch (Exception ex) { Log("Error exportando XLSX: " + ex.Message); }
    }

    /// <summary>
    /// Exporta el modelo de vigas continuas del proyecto (de todos los niveles
    /// de todos los edificios) y la base de cargas al formato abierto
    /// <b>SAF (Structural Analysis Format)</b> en <paramref name="path"/>.
    /// </summary>
    public void ExportarSaf(string path)
    {
        try
        {
            var vigas = _proyecto.Edificios
                .SelectMany(e => e.Niveles)
                .SelectMany(n => n.Vigas)
                .ToList();
            SafExporter.Export(vigas, _proyecto.Combinaciones, path, _proyecto.Nombre);
            Log($"SAF exportado ({vigas.Count} viga(s)): {path}");
        }
        catch (Exception ex) { Log("Error exportando SAF: " + ex.Message); }
    }

    public IReadOnlyList<Norma> Normas => ReglamentoService.Load();

    private static Sistema NuevoSistemaDemo()
    {
        var s = new Sistema { Nombre = "Sistema No 1", Fc = 0.210, Fy = 4.200, Adicionales = 1 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 40, Carga = 2.000, Espesor = 0.120, Lx = 4.000, Ly = 3.500, Rec = 0.020 });
        s.Losas.Add(new Losa { Id = 2, Tipo = 22, Carga = 2.000, Espesor = 0.120, Lx = 3.500, Ly = 3.500, Rec = 0.020 });
        s.Losas.Add(new Losa { Id = 3, Tipo = 21, Carga = 2.000, Espesor = 0.120, Lx = 4.000, Ly = 4.000, Rec = 0.020 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });
        s.BordesY.Add(new BordeAdic { BI = 1, BJ = 3, Balanceo = "S" });
        return s;
    }

    // =====================================================================
    // PERSISTENCIA .lpx.json (commit 32) — formato single-file compartido
    // con MemoriaPlus.App vía ProyectoSerializer + ProyectoRegistry de Core.
    // =====================================================================

    /// <summary>
    /// Crea un proyecto vacío con un sistema demo. Reemplaza el activo.
    /// El proyecto queda en memoria sin path hasta que el usuario Guarde.
    /// </summary>
    public void NuevoProyectoLpx()
    {
        _proyecto.Sistemas.Clear();
        _proyecto.Archivo = "";
        _proyecto.Nombre = "Proyecto sin título";
        var demo = NuevoSistemaDemo();
        _proyecto.Sistemas.Add(demo);
        SistemaActivo = demo;
        StatusPersistencia = "Nuevo proyecto creado.";
        Log("Nuevo proyecto .lpx en memoria.");
        OnPropertyChanged(nameof(TituloVentana));
        OnPropertyChanged(nameof(Proyecto));
    }

    /// <summary>
    /// Abre un OpenFileDialog filtrado a <c>*.lpx.json</c> y carga el archivo
    /// elegido. Cancelar = no-op. Errores se loggean y se reflejan en
    /// <see cref="StatusPersistencia"/>.
    /// </summary>
    public async void AbrirProyectoLpxDialog()
    {
        var ruta = await MemoriaPlus.Services.AppServices.Dialogs.OpenFileAsync(
            "Abrir proyecto LosasPlus",
            new MemoriaPlus.Services.FileFilter("Proyecto LosasPlus", new[] { "*.lpx.json" }),
            new MemoriaPlus.Services.FileFilter("JSON", new[] { "*.json" }),
            new MemoriaPlus.Services.FileFilter("Todos", new[] { "*.*" }));
        if (ruta is not null) AbrirProyectoLpxPorPath(ruta);
    }

    /// <summary>
    /// Carga el .lpx.json desde el path dado vía <see cref="ProyectoSerializer"/>.
    /// Reemplaza el proyecto activo, actualiza el registry de recientes y
    /// dispara los OnPropertyChanged necesarios para refrescar la UI.
    /// </summary>
    public void AbrirProyectoLpxPorPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path))
        {
            ProyectoRegistry.Remove(path);
            RecargarProyectosRecientes();
            StatusPersistencia = $"Archivo no existe: {Path.GetFileName(path)}";
            return;
        }
        try
        {
            var p = ProyectoSerializer.Load(path);
            _proyecto.Sistemas.Clear();
            foreach (var s in p.Sistemas) _proyecto.Sistemas.Add(s);
            _proyecto.Archivo     = p.Archivo;
            _proyecto.Nombre      = p.Nombre;
            _proyecto.Autor       = p.Autor;
            _proyecto.CodigoObra  = p.CodigoObra;
            _proyecto.Ubicacion   = p.Ubicacion;
            _proyecto.Descripcion = p.Descripcion;
            SistemaActivo = _proyecto.Sistemas.FirstOrDefault() ?? NuevoSistemaDemo();

            ActualizarRecents();
            StatusPersistencia = $"Cargado: {Path.GetFileName(path)}";
            Log($"Proyecto .lpx cargado: {path} ({_proyecto.Sistemas.Count} sistema(s)).");
            OnPropertyChanged(nameof(TituloVentana));
            OnPropertyChanged(nameof(Proyecto));
        }
        catch (Exception ex)
        {
            StatusPersistencia = $"Error al abrir: {ex.Message}";
            Log("Error abriendo .lpx: " + ex.Message);
        }
    }

    /// <summary>
    /// Guarda al path actual (Proyecto.Archivo); si no hay, delega en
    /// <see cref="GuardarComoLpx"/>. Bound a Ctrl+S.
    /// </summary>
    public void GuardarProyectoLpx()
    {
        if (string.IsNullOrEmpty(_proyecto.Archivo)) { GuardarComoLpx(); return; }
        try
        {
            // Backup ANTES del Save, sobre el archivo existente (preserva
            // el snapshot pre-save). Sin esto perderíamos la versión vieja.
            MaybeBackup();
            ProyectoSerializer.Save(_proyecto, _proyecto.Archivo);
            ActualizarRecents();
            StatusPersistencia = $"Guardado: {Path.GetFileName(_proyecto.Archivo)}";
            Log($"Proyecto .lpx guardado en {_proyecto.Archivo}.");
        }
        catch (Exception ex)
        {
            StatusPersistencia = $"Error al guardar: {ex.Message}";
            Log("Error guardando .lpx: " + ex.Message);
        }
    }

    /// <summary>Pregunta destino con SaveFileDialog y guarda. Bound a Ctrl+Shift+S.</summary>
    public async void GuardarComoLpx()
    {
        var ruta = await MemoriaPlus.Services.AppServices.Dialogs.SaveFileAsync(
            "Guardar proyecto LosasPlus", SugerirNombreLpx(), ProyectoSerializer.Extension,
            new MemoriaPlus.Services.FileFilter("Proyecto LosasPlus", new[] { "*.lpx.json" }),
            new MemoriaPlus.Services.FileFilter("JSON", new[] { "*.json" }));
        if (ruta is null) return;
        try
        {
            ProyectoSerializer.Save(_proyecto, ruta);
            _proyecto.Archivo = ruta;
            ActualizarRecents();
            StatusPersistencia = $"Guardado: {Path.GetFileName(ruta)}";
            Log($"Proyecto .lpx guardado en {ruta}.");
            OnPropertyChanged(nameof(TituloVentana));
        }
        catch (Exception ex)
        {
            StatusPersistencia = $"Error al guardar: {ex.Message}";
            Log("Error guardando .lpx: " + ex.Message);
        }
    }

    private void ActualizarRecents()
    {
        if (string.IsNullOrEmpty(_proyecto.Archivo)) return;
        ProyectoRegistry.AddOrUpdate(
            _proyecto.Archivo,
            string.IsNullOrEmpty(_proyecto.Nombre) ? Path.GetFileNameWithoutExtension(_proyecto.Archivo) : _proyecto.Nombre,
            _proyecto.Autor ?? "",
            _proyecto.CodigoObra ?? "",
            _proyecto.Sistemas.Count);
        RecargarProyectosRecientes();
    }

    private void RecargarProyectosRecientes()
    {
        ProyectosRecientes.Clear();
        foreach (var e in ProyectoRegistry.Load())
        {
            ProyectosRecientes.Add(new MemoriaPlusVm.ProyectoResumen(
                e.NombreProyecto,
                e.Ingeniero,
                e.Codia,
                e.CantidadNiveles,
                e.UltimoAccesoUtc.ToLocalTime().ToString("dd/MM/yy"),
                "Guardado",
                e.Path));
        }
    }

    /// <summary>
    /// Suscribe el handler de re-validación a las losas existentes y a futuras
    /// adiciones, de cada sistema. Disparado en arranque y cuando cambia la
    /// lista de sistemas. Asegura que el chip / panel de validación refleje
    /// los cambios live mientras el usuario edita.
    /// </summary>
    private void SuscribirRevalidacionEnSistemas()
    {
        foreach (var s in _proyecto.Sistemas)
        {
            s.Losas.CollectionChanged -= OnLosasOfSistemaChanged;
            s.Losas.CollectionChanged += OnLosasOfSistemaChanged;
            foreach (var l in s.Losas)
            {
                l.PropertyChanged -= OnLosaPropChangedForRevalidacion;
                l.PropertyChanged += OnLosaPropChangedForRevalidacion;
            }
        }
    }

    private void OnLosasOfSistemaChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (Losa l in e.NewItems) l.PropertyChanged += OnLosaPropChangedForRevalidacion;
        if (e.OldItems is not null)
            foreach (Losa l in e.OldItems) l.PropertyChanged -= OnLosaPropChangedForRevalidacion;
        Validacion.Revalidar();
        Busqueda?.Refrescar();
    }

    private void OnLosaPropChangedForRevalidacion(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Losa.Lx) or nameof(Losa.Ly)
                           or nameof(Losa.Espesor) or nameof(Losa.Tipo)
                           or nameof(Losa.Carga))
        {
            Validacion.Revalidar();
        }
    }

    /// <summary>
    /// Cambia el SistemaActivo al sistema con nombre dado y entra al modo
    /// Editor. Llamado por el callback de búsqueda global.
    /// </summary>
    private void BuscarYActivarSistema(string nombreSistema)
    {
        var s = _proyecto.Sistemas.FirstOrDefault(s => s.Nombre == nombreSistema);
        if (s is null) return;
        SistemaActivo = s;
        ModoActivo = ModoSidebar.Editor;
    }

    // =====================================================================
    // GENERACIÓN DE MEMORIA .docx (commit 36)
    // =====================================================================

    /// <summary>
    /// Lanza MemoriaPlus.exe con el .lpx.json actual como argumento para que
    /// el usuario continúe el flujo de generación en la app dedicada
    /// (plantillas, perfil del ingeniero, generación, etc.).
    ///
    /// <para>
    /// Diseño: LosasPlus se enfoca en el modelo del sistema de losas
    /// (entrada al motor F. Perdomo). MemoriaPlus se enfoca en producir la
    /// memoria .docx. Compartir el .lpx.json + el registry de proyectos
    /// recientes en %APPDATA% mantiene a ambas apps en sync sin duplicar
    /// la lógica de generación.
    /// </para>
    /// </summary>
    public void GenerarMemoria()
    {
        try
        {
            // Si el proyecto no está guardado, forzar guardar como.
            if (string.IsNullOrEmpty(_proyecto.Archivo))
            {
                Log("Guardando proyecto antes de abrir MemoriaPlus...");
                GuardarComoLpx();
                if (string.IsNullOrEmpty(_proyecto.Archivo))
                {
                    StatusGeneracion = "Cancelado — necesitás guardar el proyecto primero.";
                    return;
                }
            }
            else
            {
                // Auto-guardar antes de abrir.
                GuardarProyectoLpx();
            }

            var memoriaExe = ResolverMemoriaPlusExe();
            if (memoriaExe is null)
            {
                StatusGeneracion = "✕ No se encontró MemoriaPlus.exe — instalalo o copialo junto a LosasPlus.exe.";
                Log("Error: MemoriaPlus.exe no encontrado.");
                return;
            }

            // Lanzar MemoriaPlus.exe con el path del .lpx.json. La otra app
            // puede leerlo de Environment.GetCommandLineArgs() en su startup
            // y abrir el proyecto directamente.
            Process.Start(new ProcessStartInfo(memoriaExe)
            {
                Arguments = $"\"{_proyecto.Archivo}\"",
                UseShellExecute = true,
            });
            StatusGeneracion = $"→ MemoriaPlus abierto con {Path.GetFileName(_proyecto.Archivo)}";
            Log($"MemoriaPlus lanzado: {memoriaExe} con {_proyecto.Archivo}");
        }
        catch (Exception ex)
        {
            StatusGeneracion = $"✕ Error: {ex.Message}";
            Log("Error abriendo MemoriaPlus: " + ex.Message);
        }
    }

    /// <summary>
    /// Localiza MemoriaPlus.exe en las ubicaciones probables:
    /// 1. Junto al LosasPlus.exe actual (instalación side-by-side).
    /// 2. ../src.Memoria/bin/Debug/net8.0-windows/MemoriaPlus.exe (dev).
    /// 3. ../src.Memoria/bin/Release/net8.0-windows/MemoriaPlus.exe (dev release).
    /// Devuelve null si no la encuentra.
    /// </summary>
    private static string? ResolverMemoriaPlusExe()
    {
        var dir = AppContext.BaseDirectory;
        // 1. Side-by-side (release packaged)
        var sidebyside = Path.Combine(dir, "MemoriaPlus.exe");
        if (File.Exists(sidebyside)) return sidebyside;

        // 2-3. Dev: subir hasta encontrar el sibling src.Memoria/
        var probe = new DirectoryInfo(dir);
        for (int i = 0; i < 6 && probe != null; i++)
        {
            var devDebug   = Path.Combine(probe.FullName, "src.Memoria", "bin", "Debug",   "net8.0-windows", "MemoriaPlus.exe");
            var devRelease = Path.Combine(probe.FullName, "src.Memoria", "bin", "Release", "net8.0-windows", "MemoriaPlus.exe");
            if (File.Exists(devDebug))   return devDebug;
            if (File.Exists(devRelease)) return devRelease;
            probe = probe.Parent;
        }
        return null;
    }

    private string SugerirNombreLpx()
    {
        var slug = (_proyecto.Nombre ?? "Proyecto")
            .Replace(' ', '_').Replace('/', '-').Replace('\\', '-');
        return $"{slug}{ProyectoSerializer.Extension}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    private readonly Predicate<object?>? _can;
    public RelayCommand(Action<object?> exec, Predicate<object?>? can = null) { _exec = exec; _can = can; }
    public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
    public void Execute(object? p) => _exec(p);
    // Port a Avalonia: WPF usaba CommandManager.RequerySuggested (auto-requery);
    // Avalonia no lo tiene → evento manual. RaiseCanExecuteChanged() lo dispara.
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged()
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
}

/// <summary>
/// Modos de navegación de la sidebar principal de LosasPlus. Antes del
/// commit 31 estos eran TabItems verticales; ahora son modos top-level
/// alineados con MemoriaPlus.
/// </summary>
public enum ModoSidebar
{
    Explorador,
    Editor,
    /// <summary>Editor visual CAD: plano DXF de referencia + losas (Fase 1.B).</summary>
    PlanoCad,
    /// <summary>Visor PDF multipágina con toolbar prev/next + zoom (H3).</summary>
    VisorPdf,
    /// <summary>Vista 3D alámbrica del edificio (Fase I — sin SharpDX).</summary>
    Vista3D,
    DLEditor,
    Salida,
    /// <summary>Diseño de aceros distribuidos (próximamente — placeholder en la UI).</summary>
    Aceros,
    /// <summary>Casos y combinaciones de carga del proyecto (Fase 2).</summary>
    CargasCombinaciones,
    /// <summary>Editor de vigas continuas y diagramas analíticos (Fase 3).</summary>
    Vigas,
    /// <summary>Editor de columnas del edificio (Fase J).</summary>
    Columnas,
    Validacion,
    /// <summary>Bajada de cargas por niveles + predimensionado de zapata (Fase J).</summary>
    BajadaCargas,
    Busqueda,
    Configuracion,
    Reglamento,
    Plugins,
    Acerca,
}

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using LosasPlus.Models;
using LosasPlus.Topologia;
using HxCamera = HelixToolkit.Wpf.SharpDX.Camera;
using HxPerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;

namespace LosasPlus.ViewModels.Viewport3D;

/// <summary>
/// Modos de interacción del cursor en el visor 3D (Módulo 2 Parte B
/// Fase 3D-II). Cuando <see cref="Seleccion"/> está activo, el viewport
/// se comporta como hasta ahora: el click selecciona un elemento. Con
/// las otras opciones, el viewport entra en modo autoría — el cursor
/// muestra un cubo guía oro que hace snap a las intersecciones de la
/// grilla estructural y el click materializa una nueva entidad (la
/// mutación de dominio llega en Parte C).
/// </summary>
public enum ModoHerramienta3D
{
    /// <summary>Selección bidireccional 3D ↔ paneles 2D (default).</summary>
    Seleccion,
    /// <summary>Cada click crea una <c>Columna</c> en el nivel activo.</summary>
    CrearColumna,
    /// <summary>Cada click crea una <c>Viga</c> en el nivel activo.</summary>
    CrearViga,
}

/// <summary>
/// ViewModel del visor 3D de la suite (Fase 3D-I1 del Plan Maestro de
/// Expansión 3D). Es de <b>sólo lectura</b>: observa el grafo del proyecto y
/// proyecta su geometría como wireframe (<see cref="LineGeometryModel3D"/>)
/// dentro de un <see cref="Viewport3DX"/> alimentado por
/// <see cref="ItemsEscena3D"/>.
///
/// <para>
/// El VM es dueño de tres recursos DirectX no triviales que se liberan en
/// <see cref="Dispose"/>:
/// <list type="bullet">
/// <item><see cref="EffectsManager"/> — buffers de shaders, texturas,
/// recursos GPU.</item>
/// <item><see cref="Camera"/> — la perspectiva inicial centrada en el
/// origen.</item>
/// <item><see cref="ItemsEscena3D"/> — colección reactiva de
/// <see cref="Element3D"/>, cada uno con su propia geometría y material en
/// GPU. Limpiar la colección equivale a soltar las referencias para que el
/// GC libere los recursos.</item>
/// </list>
/// </para>
///
/// <para>
/// La traducción dominio → mallas vive en <see cref="SyncEscenaService"/>,
/// que recibe el <see cref="Proyecto"/> y la colección y la rellena en hilo
/// secundario + Dispatcher.Invoke (ver detalles en el servicio).
/// </para>
/// </summary>
public sealed class Viewport3DViewModel : INotifyPropertyChanged, IDisposable
{
    private bool _disposed;
    private bool _cargandoEscena;
    private HxCamera _camera;
    private SeleccionService? _seleccionService;
    private ModoHerramienta3D _herramientaActiva = ModoHerramienta3D.Seleccion;
    private MainViewModel? _mainViewModel;

    /// <summary>
    /// Construye el VM con instancias frescas de DirectX. La cámara arranca
    /// mirando hacia el origen desde una posición isométrica conservadora
    /// (20 m del centro), con +Z como "arriba" para alinearse con la
    /// convención CSI (ETABS/SAP/SAF) que usaremos en Fase 3D-I2 y SAF.
    /// </summary>
    public Viewport3DViewModel()
    {
        EffectsManager = new DefaultEffectsManager();
        // HelixToolkit.Wpf.SharpDX.PerspectiveCamera expone Position/LookDirection
        // /UpDirection como Point3D/Vector3D de System.Windows.Media.Media3D
        // (los tipos legacy de WPF). Compatible con el XAML existente.
        _camera = new HxPerspectiveCamera
        {
            Position          = new Point3D(20, -20, 20),
            LookDirection     = new Vector3D(-20, 20, -20),
            UpDirection       = new Vector3D(0, 0, 1),
            FarPlaneDistance  = 1000.0,
            NearPlaneDistance = 0.1,
            FieldOfView       = 45.0,
        };
        ItemsEscena3D = new ObservableElement3DCollection();
    }

    /// <summary>
    /// Manager de efectos DirectX que el <see cref="Viewport3DX"/> usa para
    /// compilar shaders, gestionar texturas y recursos de GPU. Es disposable;
    /// se libera en <see cref="Dispose"/> para evitar fugas de memoria de
    /// vídeo entre re-aperturas del proyecto.
    /// </summary>
    public IEffectsManager EffectsManager { get; }

    /// <summary>
    /// Cámara del viewport. Por defecto <see cref="HxPerspectiveCamera"/>
    /// situada en (20, −20, 20) m mirando al origen con eje +Z arriba.
    /// El setter es público para futuros toggles de Planta/Elevación/3D
    /// (Fase 3D-I3 o posterior).
    /// </summary>
    public HxCamera Camera
    {
        get => _camera;
        set
        {
            if (ReferenceEquals(_camera, value)) return;
            _camera = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Colección reactiva de elementos visuales 3D. El <see cref="Viewport3DX"/>
    /// la consume vía <c>&lt;hx:GroupModel3D ItemsSource="{Binding ItemsEscena3D}"/&gt;</c>.
    ///
    /// <para>
    /// <b>Gotcha</b>: HelixToolkit prohíbe reciclar instancias de
    /// <see cref="Element3D"/> entre regeneraciones — re-adjuntar el mismo
    /// nodo a otro padre lanza <see cref="ArgumentException"/>. Por eso
    /// <see cref="RegenerarEscenaAsync"/> hace <c>Clear()</c> y construye
    /// nuevas instancias en cada llamada en lugar de reutilizarlas.
    /// </para>
    /// </summary>
    public ObservableElement3DCollection ItemsEscena3D { get; }

    /// <summary>
    /// <c>true</c> mientras el motor del servicio regenera la escena en hilo
    /// secundario. Bound al <c>ProgressBar IsIndeterminate</c> de la toolbar
    /// del <see cref="Views.Viewport3D.Viewport3DView"/>.
    /// </summary>
    public bool CargandoEscena
    {
        get => _cargandoEscena;
        private set
        {
            if (_cargandoEscena == value) return;
            _cargandoEscena = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Servicio singleton de selección sincronizada (Fase 3D-I3). El
    /// <c>MainViewModel</c> lo inyecta tras instanciar el VM. Cuando se
    /// asigna, el VM se suscribe al evento <c>OnSeleccionCambiada</c>
    /// para actualizar el resaltado visual en caliente sin reconstruir
    /// la escena. Asignar <c>null</c> desuscribe limpiamente.
    /// </summary>
    public SeleccionService? SeleccionService
    {
        get => _seleccionService;
        set
        {
            if (ReferenceEquals(_seleccionService, value)) return;
            // Desuscribir del servicio anterior (si lo hay).
            if (_seleccionService is not null)
                _seleccionService.OnSeleccionCambiada -= AplicarResaltadoVisual;
            _seleccionService = value;
            // Suscribir al nuevo + sincronizar inmediatamente el estado actual.
            if (_seleccionService is not null)
            {
                _seleccionService.OnSeleccionCambiada += AplicarResaltadoVisual;
                AplicarResaltadoVisual(_seleccionService.ElementoSeleccionado);
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Herramienta de interacción activa en el viewport 3D (Módulo 2
    /// Parte B Fase 3D-II). <see cref="ModoHerramienta3D.Seleccion"/>
    /// por defecto — preserva el comportamiento de Fase 3D-I3. Las
    /// otras opciones habilitan el cursor guía oro + snap a grilla en
    /// el code-behind del <c>Viewport3DView</c>.
    /// </summary>
    public ModoHerramienta3D HerramientaActiva
    {
        get => _herramientaActiva;
        set
        {
            if (_herramientaActiva == value) return;
            _herramientaActiva = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Referencia al <see cref="LosasPlus.ViewModels.MainViewModel"/>
    /// padre (Módulo 2 Parte B Fase 3D-II) — la inyecta el ctor del
    /// shell para que el code-behind del visor 3D pueda leer
    /// <c>NivelActivo</c> y <c>Proyecto.Edificios</c> sin colgarse de
    /// <c>Application.Current</c>. Opcional para que el VM siga siendo
    /// construible standalone en tests xUnit.
    /// </summary>
    public MainViewModel? MainViewModel
    {
        get => _mainViewModel;
        set
        {
            if (ReferenceEquals(_mainViewModel, value)) return;
            _mainViewModel = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Publica una nueva selección al <see cref="SeleccionService"/> si
    /// está inyectado. Es llamado por el code-behind del viewport cuando
    /// el <c>MouseDown3D</c> resuelve un <see cref="DomainKey"/> sobre un
    /// modelo de la escena.
    /// </summary>
    /// <param name="seleccionado">Llave del elemento clickeado, o <c>null</c> para deseleccionar.</param>
    /// <param name="emisor">Origen del cambio (típicamente la View 3D).</param>
    public void NotificarSeleccionDesde3D(DomainKey? seleccionado, object? emisor)
    {
        _seleccionService?.FijarSeleccion(seleccionado, emisor);
    }

    /// <summary>
    /// Aplica el resaltado cromático sobre la colección visible. Se invoca
    /// como handler del evento <c>OnSeleccionCambiada</c> y también
    /// internamente tras regenerar la escena (el grafo nuevo debe respetar
    /// la selección activa).
    /// </summary>
    private void AplicarResaltadoVisual(DomainKey? seleccionado)
    {
        if (_disposed) return;
        SyncEscenaService.ActualizarResaltadoVisual(ItemsEscena3D, seleccionado);
    }

    /// <summary>
    /// Regenera la escena 3D desde un <paramref name="proyecto"/> dado.
    /// Limpia <see cref="ItemsEscena3D"/> y delega al
    /// <see cref="SyncEscenaService"/> para que sintetice las mallas de
    /// alambre. Patrón fire-and-forget en el shell: <c>MainViewModel</c> lo
    /// invoca sin esperar al cambiar a <c>ModoSidebar.Vista3D</c>.
    ///
    /// <para>
    /// Si <paramref name="proyecto"/> es <c>null</c> la escena queda vacía
    /// (caso defensivo durante la construcción inicial del shell antes de
    /// que el <c>Proyecto</c> esté instanciado).
    /// </para>
    /// </summary>
    public async Task RegenerarEscenaAsync(Proyecto? proyecto)
    {
        if (_disposed) return;
        CargandoEscena = true;
        try
        {
            // Gotcha de HelixToolkit: nunca reciclar instancias de Element3D —
            // cada regeneración limpia la colección y construye nuevas mallas.
            ItemsEscena3D.Clear();
            if (proyecto is null) return;
            await SyncEscenaService.GenerarMallasProyectoAsync(proyecto, ItemsEscena3D)
                .ConfigureAwait(true);

            // Tras la regeneración, restituir el resaltado del elemento
            // actualmente seleccionado (si lo hay) para no perder el estado
            // visual de la última interacción del usuario.
            if (_seleccionService is not null)
                AplicarResaltadoVisual(_seleccionService.ElementoSeleccionado);
        }
        catch (Exception)
        {
            // En esta iteración el viewport es sólo lectura: ante un error de
            // proyección, se deja la escena vacía en vez de propagar y romper
            // el shell. Las iteraciones posteriores (selección, edición)
            // necesitarán logging dirigido.
            ItemsEscena3D.Clear();
        }
        finally
        {
            CargandoEscena = false;
        }
    }

    /// <summary>
    /// Libera el <see cref="EffectsManager"/> y vacía la colección de
    /// <see cref="Element3D"/> para soltar los recursos DirectX en GPU.
    /// El shell debe invocar este <see cref="Dispose"/> al cerrar la
    /// ventana principal (suscribir a <c>Window.Closed</c>).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // Desuscribir del servicio de selección antes de tirar el VM
            // para evitar callbacks sobre un estado parcialmente disposed.
            if (_seleccionService is not null)
                _seleccionService.OnSeleccionCambiada -= AplicarResaltadoVisual;
            ItemsEscena3D.Clear();
            EffectsManager.Dispose();
        }
        catch
        {
            // Liberación best-effort: el shell ya está cerrándose.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

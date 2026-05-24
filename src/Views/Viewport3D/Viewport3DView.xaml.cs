using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using LosasPlus.Grillas;
using LosasPlus.Topologia;
using LosasPlus.ViewModels.Viewport3D;

namespace LosasPlus.Views.Viewport3D;

/// <summary>
/// Code-behind del visor 3D (Fase 3D-I1 + I3 del Plan Maestro). Inicializa
/// el XAML y enruta el evento <c>MouseDown3D</c> del <c>Viewport3DX</c>
/// hacia el <see cref="Viewport3DViewModel"/>, que a su vez delega al
/// <see cref="LosasPlus.ViewModels.SeleccionService"/> singleton.
///
/// <para>
/// El raycasting de selección se hace en code-behind porque <c>MouseDown3D</c>
/// trae los <c>HitTestResult</c> nativos de HelixToolkit y depende de
/// tipos visuales (<c>Element3D</c>) — mantener esa traducción en el VM
/// rompería el aislamiento MVVM. El code-behind sólo extrae el
/// <see cref="DomainKey"/> del <c>Element3D.Tag</c> y notifica al VM.
/// </para>
/// </summary>
public partial class Viewport3DView : UserControl
{
    public Viewport3DView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handler del evento <c>MouseDown3D</c> del <c>Viewport3DX</c>:
    /// inspecciona el <see cref="HitTestResult"/> nativo y, si el modelo
    /// impactado lleva un <see cref="DomainKey"/> en su <c>Tag</c>,
    /// notifica al <see cref="Viewport3DViewModel"/> que enrutará al
    /// <c>SeleccionService</c>.
    ///
    /// <para>
    /// Si el click cae en vacío (sin <see cref="HitTestResult"/>) o el
    /// modelo impactado no tiene tag estructural, el handler es no-op —
    /// no deselecciona porque eso obligaría al usuario a re-seleccionar
    /// constantemente al rotar la cámara. La deselección explícita se
    /// hará vía un botón de toolbar o tecla Escape en iteraciones
    /// posteriores.
    /// </para>
    /// </summary>
    private async void OnViewportMouseDown3D(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Viewport3DViewModel vm) return;
        if (e is not MouseDown3DEventArgs args) return;

        // ----- Modo Selección (Fase 3D-I3) — comportamiento legacy preservado.
        if (vm.HerramientaActiva == ModoHerramienta3D.Seleccion)
        {
            if (args.HitTestResult is null) return;
            if (args.HitTestResult.ModelHit is Element3D modelo && modelo.Tag is DomainKey key)
                vm.NotificarSeleccionDesde3D(key, this);
            return;
        }

        // ----- Modo CrearColumna (Módulo 2C Fase 3D-II) — muta el dominio
        // y refresca la escena instantáneamente.
        if (vm.HerramientaActiva != ModoHerramienta3D.CrearColumna) return;

        var main = vm.MainViewModel;
        if (main is null) return;
        var nivel = main.NivelActivo;
        var edificio = main.Proyecto?.Edificios?.FirstOrDefault();
        if (edificio is null || args.Viewport is null) return;

        // Proyectar el click al plano horizontal Z = nivel.Cota.
        var p3 = args.Viewport.UnProjectOnPlane(
            args.Position, new Point3D(0, 0, nivel.Cota), new Vector3D(0, 0, 1));
        if (!p3.HasValue) return;

        // Mutación atómica del dominio: snap + Id autoincremental +
        // columna + (zapata si nivel base).
        GridCreationEngine.CrearColumnaConSnap(
            nivel, edificio.Grillas, p3.Value.X, p3.Value.Y);

        // Refresco asíncrono de la escena: el visor re-renderiza con el
        // nuevo elemento (la columna aparece extruida instantáneamente
        // en la posición magnetizada).
        await vm.RegenerarEscenaAsync(main.Proyecto);
    }

    // ===================================================================
    // CURSOR GUÍA + SNAP (Módulo 2 Parte B Fase 3D-II)
    // ===================================================================

    /// <summary>
    /// Marca <c>true</c> tras la primera invocación de
    /// <see cref="InicializarCursorGuia"/> — la geometría y el material
    /// del cubo guía sólo se construyen una vez y se reutilizan en
    /// cada <c>MouseMove3D</c>. Sin esto reconstruiríamos la malla
    /// cientos de veces por segundo.
    /// </summary>
    private bool _cursorGuiaInicializado;

    /// <summary>
    /// Construye perezosamente la geometría + material del cubo cursor
    /// guía (cubo de 0.25 m con <see cref="SyncEscenaService.MaterialCursorGuia"/>
    /// = oro 50% alpha). Idempotente — segundas invocaciones son no-op.
    /// </summary>
    private void InicializarCursorGuia()
    {
        if (_cursorGuiaInicializado) return;
        CursorGuia3D.Geometry = SyncEscenaService.ConstruirCajaUnitaria(ladoM: 0.25f);
        CursorGuia3D.Material = SyncEscenaService.MaterialCursorGuia;
        CursorGuia3D.CullMode = global::SharpDX.Direct3D11.CullMode.Back;
        CursorGuia3D.FrontCounterClockwise = true;
        _cursorGuiaInicializado = true;
    }

    /// <summary>
    /// Handler del evento <c>MouseMove3D</c> del <c>Viewport3DX</c>
    /// (Módulo 2 Parte B Fase 3D-II). Si la herramienta activa es
    /// distinta de <see cref="ModoHerramienta3D.Seleccion"/>:
    ///
    /// <list type="number">
    ///   <item>Proyecta la posición del mouse al plano horizontal
    ///   <c>Z = NivelActivo.Cota</c> vía
    ///   <c>Viewport3DX.UnProjectOnPlane</c>.</item>
    ///   <item>Invoca <see cref="GridSnapEngine.CalcularSnapAGrilla"/>
    ///   con la grilla del primer edificio del proyecto.</item>
    ///   <item>Mueve el <c>CursorGuia3D</c> mediante un
    ///   <see cref="TranslateTransform3D"/> a la coordenada
    ///   resultante (magnetizada si está dentro del radio o libre
    ///   si no) y lo hace visible.</item>
    /// </list>
    ///
    /// <para>
    /// En modo <see cref="ModoHerramienta3D.Seleccion"/> el cursor se
    /// oculta (no-op). Si el VM padre no está cableado o no hay nivel
    /// activo, se hace early-return sin riesgo.
    /// </para>
    /// </summary>
    private void OnViewportMouseMove3D(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Viewport3DViewModel vm) return;

        // Modo Selección → cursor oculto, sin proyección.
        if (vm.HerramientaActiva == ModoHerramienta3D.Seleccion)
        {
            CursorGuia3D.Visibility = Visibility.Hidden;
            return;
        }

        // Resolver nivel activo y grilla del primer edificio. Es la
        // convención M2: edificio[0] es el activo (multi-edificio se
        // arquitecta en una fase futura).
        var main = vm.MainViewModel;
        if (main is null) return;
        var nivel = main.NivelActivo;
        var edificio = main.Proyecto?.Edificios?.FirstOrDefault();
        if (edificio is null) return;

        if (e is not MouseMove3DEventArgs args || args.Viewport is null) return;

        // Proyectar el mouse al plano horizontal Z=Cota del nivel activo.
        // UnProjectOnPlane requiere un punto del plano + su normal (+Z).
        var planoPunto  = new Point3D(0, 0, nivel.Cota);
        var planoNormal = new Vector3D(0, 0, 1);
        var p3 = args.Viewport.UnProjectOnPlane(args.Position, planoPunto, planoNormal);
        if (!p3.HasValue) return;

        // Snap a la intersección de la grilla más cercana (dentro de
        // tolerancia) o conservar la posición libre.
        var (x, y, _) = GridSnapEngine.CalcularSnapAGrilla(
            p3.Value.X, p3.Value.Y, edificio.Grillas);

        // Asegurar que la geometría + material existan + mover el cubo
        // y hacerlo visible.
        InicializarCursorGuia();
        CursorGuia3D.Transform   = new TranslateTransform3D(x, y, nivel.Cota);
        CursorGuia3D.Visibility  = Visibility.Visible;
    }
}

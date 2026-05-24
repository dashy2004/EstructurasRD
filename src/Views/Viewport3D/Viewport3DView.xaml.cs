using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using LosasPlus.Comandos;
using LosasPlus.Grillas;
using LosasPlus.Topologia;
using LosasPlus.ViewModels;
using LosasPlus.ViewModels.Viewport3D;
using NumericsVector3 = System.Numerics.Vector3;

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
    // ===================================================================
    // ESTADO INTERNO DEL FLOW 2-CLICK PARA CREARVIGA (Liga B B3)
    // ===================================================================

    /// <summary>
    /// Primer endpoint memorizado durante el flow 2-click de CrearViga.
    /// <c>null</c> cuando no hay click pendiente. Tras el segundo click,
    /// se resetea a <c>null</c> y se oculta el preview line.
    /// </summary>
    private (double X, double Y)? _vigaP1;

    private async void OnViewportMouseDown3D(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Viewport3DViewModel vm) return;
        if (e is not MouseDown3DEventArgs args) return;
        var main = vm.MainViewModel;

        // ----- Modo Selección (Fase 3D-I3) — comportamiento legacy preservado.
        if (vm.HerramientaActiva == ModoHerramienta3D.Seleccion)
        {
            if (args.HitTestResult is null) return;
            if (args.HitTestResult.ModelHit is Element3D modelo && modelo.Tag is DomainKey key)
                vm.NotificarSeleccionDesde3D(key, this);
            return;
        }

        // ----- Modo BorrarElemento (Liga B B3) — 1 click sobre Element3D
        // con Tag DomainKey dispara el comando resiliente del MainViewModel.
        // Click en vacío es no-op silencioso. El servicio aplica Ghost
        // Deletion: éxito silencioso si la entidad ya no existe.
        if (vm.HerramientaActiva == ModoHerramienta3D.BorrarElemento)
        {
            if (main is null) return;
            if (args.HitTestResult?.ModelHit is Element3D mod && mod.Tag is DomainKey k)
            {
                var borrarArgs = new BorrarElementoCommandArgs(Guid.NewGuid(), k);
                main.BorrarElementoCommand?.Execute(borrarArgs);
            }
            return;
        }

        // ----- Modos CrearColumna y CrearViga necesitan proyectar al plano del nivel.
        if (main is null) return;
        var nivel = main.NivelActivo;
        var edificio = main.Proyecto?.Edificios?.FirstOrDefault();
        if (edificio is null || args.Viewport is null) return;

        var p3 = args.Viewport.UnProjectOnPlane(
            args.Position, new Point3D(0, 0, nivel.Cota), new Vector3D(0, 0, 1));
        if (!p3.HasValue) return;

        // ----- Modo CrearColumna (Módulo 2C Fase 3D-II) — preservado.
        if (vm.HerramientaActiva == ModoHerramienta3D.CrearColumna)
        {
            GridCreationEngine.CrearColumnaConSnap(
                nivel, edificio.Grillas, p3.Value.X, p3.Value.Y);
            await vm.RegenerarEscenaAsync(main.Proyecto);
            return;
        }

        // ----- Modo CrearViga (Liga B B3) — state machine 2-click.
        if (vm.HerramientaActiva == ModoHerramienta3D.CrearViga)
        {
            if (_vigaP1 is null)
            {
                // Primer click: memorizar P1. El preview live se renderiza
                // en OnViewportMouseMove3D mientras _vigaP1 != null.
                _vigaP1 = (p3.Value.X, p3.Value.Y);
                return;
            }
            // Segundo click: emitir comando resiliente al MainViewModel
            // (validación + idempotencia + mutación vía IAutoria3DService +
            // sweep en caso de fallo). El handler limpia el preview y el
            // memo del primer click ANTES de emitir, para que un error
            // del comando no deje estado huérfano.
            var p1 = _vigaP1.Value;
            _vigaP1 = null;
            VigaPreviewLine.Visibility = Visibility.Hidden;

            var cmdArgs = new CrearVigaCommandArgs(
                Guid.NewGuid(),
                p1.X, p1.Y,
                p3.Value.X, p3.Value.Y);
            main.CrearVigaCommand?.Execute(cmdArgs);
        }
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

        // Modo Selección o Borrar → cursor oculto, sin proyección,
        // limpiar memo del primer click si veníamos de CrearViga.
        if (vm.HerramientaActiva == ModoHerramienta3D.Seleccion
            || vm.HerramientaActiva == ModoHerramienta3D.BorrarElemento)
        {
            CursorGuia3D.Visibility = Visibility.Hidden;
            VigaPreviewLine.Visibility = Visibility.Hidden;
            _vigaP1 = null;
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

        // Asegurar que la geometría + material del cubo guía existan +
        // mover el cubo y hacerlo visible.
        InicializarCursorGuia();
        CursorGuia3D.Transform   = new TranslateTransform3D(x, y, nivel.Cota);
        CursorGuia3D.Visibility  = Visibility.Visible;

        // ---- Preview live de viga durante 2-click (Liga B B3) ----
        // Si estamos en CrearViga con el primer click ya memorizado,
        // dibujar segmento desde P1 hasta el cursor snap-magnetizado.
        if (vm.HerramientaActiva == ModoHerramienta3D.CrearViga && _vigaP1 is not null)
        {
            var p1 = _vigaP1.Value;
            VigaPreviewLine.Geometry = SyncEscenaService.ConstruirLineaProvisional(
                new NumericsVector3((float)p1.X, (float)p1.Y, (float)nivel.Cota),
                new NumericsVector3((float)x,    (float)y,    (float)nivel.Cota));
            VigaPreviewLine.Visibility = Visibility.Visible;
        }
        else
        {
            VigaPreviewLine.Visibility = Visibility.Hidden;
        }
    }
}

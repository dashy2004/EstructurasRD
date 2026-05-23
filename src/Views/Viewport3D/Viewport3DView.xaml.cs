using System.Windows;
using System.Windows.Controls;
using HelixToolkit.Wpf.SharpDX;
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
    private void OnViewportMouseDown3D(object sender, RoutedEventArgs e)
    {
        if (DataContext is not Viewport3DViewModel vm) return;
        if (e is not MouseDown3DEventArgs args) return;
        if (args.HitTestResult is null) return;

        if (args.HitTestResult.ModelHit is Element3D modelo && modelo.Tag is DomainKey key)
        {
            vm.NotificarSeleccionDesde3D(key, this);
        }
    }
}

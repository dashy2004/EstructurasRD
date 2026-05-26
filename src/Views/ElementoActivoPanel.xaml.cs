using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LosasPlus.Views;

/// <summary>
/// Panel persistente del elemento estructural activo (Liga B paso B2),
/// ahora <b>movible + minimizable</b>:
///
/// <list type="bullet">
///   <item>Arrastra el header (cursor SizeAll) para reposicionar el
///   panel donde quieras dentro del shell — el handler modifica el
///   <see cref="FrameworkElement.Margin"/> en respuesta a MouseMove.</item>
///   <item>Click en el botón [—] / [□] del header o doble-click en el
///   header alternan minimizar/restaurar el body. Cuando está
///   minimizado, sólo queda visible la barra superior con tipo + Id
///   (~30 px de alto).</item>
/// </list>
///
/// <para>
/// La posición se mantiene durante la sesión de la app. La
/// persistencia entre sesiones (preferencias.json) queda para una
/// iteración futura.
/// </para>
/// </summary>
public partial class ElementoActivoPanel : UserControl
{
    public ElementoActivoPanel()
    {
        InitializeComponent();
    }

    // ===== Estado del drag =====
    private bool _isDragging;
    private Point _dragStartPoint;
    private Thickness _initialMargin;
    private bool _isMinimized;

    /// <summary>
    /// MouseDown en el header: si es doble-click → toggle minimize.
    /// Sino → captura mouse + memoriza el punto inicial para empezar
    /// a arrastrar.
    /// </summary>
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMinimize();
            e.Handled = true;
            return;
        }
        // El padre del panel es típicamente un Grid (Grid.Row=2 del
        // inner grid del MainWindow) — usamos coordenadas del padre
        // para que el delta del drag sea consistente con el Margin.
        if (Parent is not IInputElement parent) return;
        _isDragging = true;
        _dragStartPoint = e.GetPosition(parent);
        _initialMargin = Margin;
        Mouse.Capture(this);
        e.Handled = true;
    }

    /// <summary>
    /// MouseMove durante el drag: calcula el delta y actualiza
    /// <see cref="FrameworkElement.Margin"/> para mover el panel.
    /// Restringe a coordenadas no-negativas (HorizontalAlignment="Left"
    /// + VerticalAlignment="Top" usan Margin.Left/Top como anclaje).
    /// </summary>
    private void OnHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        if (Parent is not IInputElement parent) return;
        var pos = e.GetPosition(parent);
        double dx = pos.X - _dragStartPoint.X;
        double dy = pos.Y - _dragStartPoint.Y;
        double newLeft = System.Math.Max(0, _initialMargin.Left + dx);
        double newTop  = System.Math.Max(0, _initialMargin.Top  + dy);
        Margin = new Thickness(newLeft, newTop, 0, 0);
    }

    /// <summary>MouseUp: libera el capture y termina el drag.</summary>
    private void OnHeaderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Mouse.Capture(null);
        e.Handled = true;
    }

    /// <summary>Click del botón [—] / [□] del header.</summary>
    private void OnToggleMinimizeClick(object sender, RoutedEventArgs e)
    {
        ToggleMinimize();
        e.Handled = true;
    }

    /// <summary>
    /// Alterna la visibilidad del body. Cuando se minimiza, el botón
    /// cambia su contenido de "—" a "□" para indicar la acción de
    /// restaurar.
    /// </summary>
    private void ToggleMinimize()
    {
        _isMinimized = !_isMinimized;
        if (Body is not null)
            Body.Visibility = _isMinimized ? Visibility.Collapsed : Visibility.Visible;
        if (BtnMinimize is not null)
        {
            BtnMinimize.Content = _isMinimized ? "□" : "—";
            BtnMinimize.ToolTip = _isMinimized
                ? "Restaurar (también doble-click en el header)"
                : "Minimizar (también doble-click en el header)";
        }
    }
}

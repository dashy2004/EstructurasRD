using Avalonia.Controls;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Vista del modo <b>Lienzo CAD</b> — port a Avalonia (Fase E.1: render). Aloja
/// el <see cref="CadCanvasHost"/> y el panel de ajustes del plano/PDF/muros.
/// El <c>DataContext</c> es el <c>MainViewModel</c> (de ahí salen <c>CadEditor</c>
/// y <c>Sistema</c>). Los editores flotantes in-canvas y su wiring de mouse
/// (doble-clic sobre losa, calibración de PDF) se portan en la Fase E.2.
/// </summary>
public partial class CadView : UserControl
{
    // InitializeComponent (no AvaloniaXamlLoader.Load): puebla los campos x:Name
    // (Canvas, PanelToggle) que el code-behind referencia.
    public CadView() => InitializeComponent();

    /// <summary>
    /// El <see cref="CadCanvasHost"/> alojado — expuesto para que <c>MainWindow</c>
    /// pueda capturar el lienzo al exportar a Excel (re-cableado en E.2).
    /// </summary>
    public CadCanvasHost CanvasHost => Canvas;
}

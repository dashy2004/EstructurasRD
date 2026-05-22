using System.Windows;

namespace LosasPlus.Views;

/// <summary>
/// Modal «Importar combinaciones desde DISEST» (Fase 2, Iteración 3). El
/// <c>DataContext</c> es un <c>ImportadorCombinacionesViewModel</c>; el cierre
/// lo maneja el callback <c>Cerrar</c> que inyecta el abridor de la ventana.
/// </summary>
public partial class ImportarCombinacionesWindow : Window
{
    public ImportarCombinacionesWindow() => InitializeComponent();
}

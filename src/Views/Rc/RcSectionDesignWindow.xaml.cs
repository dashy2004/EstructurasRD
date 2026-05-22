using System.Windows;

namespace LosasPlus.Views.Rc;

/// <summary>
/// Ventana modal que aloja el editor de diseño de sección RC (Fase 4,
/// Iteración 2). La abre el editor de vigas para el tramo seleccionado; su
/// <c>DataContext</c> es el <c>RcSectionDesignViewModel</c> hijo.
/// </summary>
public partial class RcSectionDesignWindow : Window
{
    public RcSectionDesignWindow() => InitializeComponent();

    private void OnCerrarClick(object sender, RoutedEventArgs e) => Close();
}

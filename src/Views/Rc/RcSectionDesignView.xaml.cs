using System.Windows.Controls;

namespace LosasPlus.Views.Rc;

/// <summary>
/// Vista del editor de diseño de sección de concreto reforzado (Fase 4,
/// Iteración 2). El code-behind no tiene lógica: la edición de cada parámetro
/// toma el snapshot de Undo en los setters de <c>RcSectionDesignViewModel</c>,
/// y el diagrama del bloque de Whitney se regenera de forma reactiva en ese
/// mismo ViewModel.
/// </summary>
public partial class RcSectionDesignView : UserControl
{
    public RcSectionDesignView() => InitializeComponent();
}

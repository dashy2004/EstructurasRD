using Avalonia.Controls;
using Avalonia.Interactivity;
using LosasPlus.ViewModels;

namespace LosasPlus.Views;

/// <summary>
/// Vista «Bajada de cargas» (Fase J.4): tabla de carga acumulada por nivel +
/// predimensionado de zapata. El botón «Recalcular» relee el edificio activo.
/// </summary>
public partial class BajadaCargasView : UserControl
{
    public BajadaCargasView()
    {
        InitializeComponent();
    }

    private void OnRecalcular(object? sender, RoutedEventArgs e)
        => (DataContext as BajadaCargasViewModel)?.Recalcular();
}

using System.Windows;
using System.Windows.Controls;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class ConfiguracionView : UserControl
{
    public ConfiguracionView() => InitializeComponent();

    /// <summary>
    /// Propaga la bandera <see cref="ConfiguracionViewModel.EsCalculadora"/> desde el
    /// host (LosasPlus / MemoriaPlus). En LosasPlus seteamos <c>EsCalculadora="True"</c>
    /// para ocultar el sub-tab "Datos del ingeniero", que es metadata de portada de
    /// memoria y no aplica a la calculadora.
    /// </summary>
    public static readonly DependencyProperty EsCalculadoraProperty =
        DependencyProperty.Register(nameof(EsCalculadora), typeof(bool), typeof(ConfiguracionView),
            new PropertyMetadata(false, OnEsCalculadoraChanged));

    public bool EsCalculadora
    {
        get => (bool)GetValue(EsCalculadoraProperty);
        set => SetValue(EsCalculadoraProperty, value);
    }

    private static void OnEsCalculadoraChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ConfiguracionView view && view.DataContext is ConfiguracionViewModel vm)
            vm.EsCalculadora = (bool)e.NewValue;
    }
}

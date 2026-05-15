using System;
using System.Windows;
using System.Windows.Controls;
using LosasPlus.Persistence;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class ConfiguracionView : UserControl
{
    public ConfiguracionView()
    {
        InitializeComponent();
        // Suscribirse al evento del VM creado por el XAML (DataContext interno).
        // Lo hacemos en Loaded porque al construir el UserControl el DataContext
        // todavía no se materializó.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguracionViewModel vm)
        {
            // Desuscribir si ya estaba conectado (Loaded puede dispararse varias veces).
            vm.AparienciaGuardada -= OnVmAparienciaGuardada;
            vm.AparienciaGuardada += OnVmAparienciaGuardada;
        }
    }

    private void OnVmAparienciaGuardada(object? sender, EventArgs e)
    {
        if (DataContext is ConfiguracionViewModel vm)
            AparienciaCambiada?.Invoke(this, new AparienciaCambiadaEventArgs(vm.Apariencia));
    }

    /// <summary>
    /// Evento bubbleado al host (MainWindow de LosasPlus o MemoriaPlus) para que
    /// aplique la apariencia al runtime — mutar ResourceDictionary global, tema
    /// activo, color de acento, tipografía. El shared assembly no puede llamar
    /// directamente a <c>LosasPlus.App.AplicarApariencia</c> (referencia circular)
    /// — el host se suscribe y lo invoca.
    /// </summary>
    public event EventHandler<AparienciaCambiadaEventArgs>? AparienciaCambiada;

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

/// <summary>
/// Event args con la <see cref="AparienciaConfig"/> actualizada que el host
/// debe aplicar al runtime.
/// </summary>
public class AparienciaCambiadaEventArgs : EventArgs
{
    public AparienciaConfig Apariencia { get; }
    public AparienciaCambiadaEventArgs(AparienciaConfig apariencia) => Apariencia = apariencia;
}

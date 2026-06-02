using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LosasPlus.Persistence;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class ConfiguracionView : UserControl
{
    public ConfiguracionView()
    {
        AvaloniaXamlLoader.Load(this);
        // El DataContext (ConfiguracionViewModel) se materializa con el XAML;
        // suscribimos en Loaded para asegurar que ya existe.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguracionViewModel vm)
        {
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
    /// Evento bubbleado al host (MainWindow) para que aplique la apariencia al
    /// runtime. El shared assembly no puede llamar directamente al host
    /// (referencia circular) — el host se suscribe.
    /// </summary>
    public event EventHandler<AparienciaCambiadaEventArgs>? AparienciaCambiada;

    /// <summary>
    /// Bandera propagada desde el host: en LosasPlus se setea True para ocultar
    /// el sub-tab "Datos del ingeniero" (metadata de portada, no aplica a la
    /// calculadora). Port a Avalonia: DependencyProperty → StyledProperty.
    /// </summary>
    public static readonly StyledProperty<bool> EsCalculadoraProperty =
        AvaloniaProperty.Register<ConfiguracionView, bool>(nameof(EsCalculadora));

    public bool EsCalculadora
    {
        get => GetValue(EsCalculadoraProperty);
        set => SetValue(EsCalculadoraProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EsCalculadoraProperty && DataContext is ConfiguracionViewModel vm)
            vm.EsCalculadora = change.GetNewValue<bool>();
    }
}

/// <summary>
/// Event args con la <see cref="AparienciaConfig"/> actualizada que el host debe
/// aplicar al runtime.
/// </summary>
public class AparienciaCambiadaEventArgs : EventArgs
{
    public AparienciaConfig Apariencia { get; }
    public AparienciaCambiadaEventArgs(AparienciaConfig apariencia) => Apariencia = apariencia;
}

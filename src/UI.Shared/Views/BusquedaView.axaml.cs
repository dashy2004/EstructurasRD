using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus.Views;

public partial class BusquedaView : UserControl
{
    public BusquedaView()
    {
        AvaloniaXamlLoader.Load(this);

        // Auto-focus del TextBox cuando la vista se hace visible — el usuario
        // puede tipear inmediatamente tras Ctrl+F o entrar al modo Búsqueda.
        // Port a Avalonia: WPF usaba IsVisibleChanged; aquí se observa la
        // propiedad IsVisible.
        this.GetObservable(IsVisibleProperty).Subscribe(new AnonymousObserver(visible =>
        {
            if (!visible) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<TextBox>("SearchBox") is { } tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            }, DispatcherPriority.Input);
        }));
    }

    /// <summary>
    /// Doble-click en una card de resultado: dispara IrAResultadoCommand con el
    /// item seleccionado. Resuelve el BusquedaViewModel en 2 saltos (DataContext
    /// directo o vía IBusquedaHost).
    /// </summary>
    private void Resultados_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not BusquedaResultado resultado) return;
        var vm = ResolverBusquedaVM();
        if (vm is null) return;
        if (vm.IrAResultadoCommand.CanExecute(resultado))
            vm.IrAResultadoCommand.Execute(resultado);
    }

    private BusquedaViewModel? ResolverBusquedaVM() => DataContext switch
    {
        BusquedaViewModel direct => direct,
        IBusquedaHost host       => host.Busqueda,
        _                        => null,
    };

    /// <summary>Observer mínimo para IObservable&lt;bool&gt; sin traer System.Reactive.</summary>
    private sealed class AnonymousObserver : IObserver<bool>
    {
        private readonly Action<bool> _onNext;
        public AnonymousObserver(Action<bool> onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(bool value) => _onNext(value);
    }
}

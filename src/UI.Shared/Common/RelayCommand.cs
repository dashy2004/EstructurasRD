using System;
using System.Windows.Input;
using Avalonia.Threading;

namespace Shared.UI.Common;

/// <summary>
/// <see cref="ICommand"/> minimalista para bindings MVVM. Empuja una <see cref="Action"/>
/// al click de un botón, con un opcional <see cref="Func{Boolean}"/> para
/// <see cref="ICommand.CanExecute"/>.
///
/// <para>
/// Port a Avalonia: WPF reevaluaba CanExecute automáticamente vía
/// <c>CommandManager.RequerySuggested</c>. Avalonia no tiene CommandManager, así
/// que <see cref="CanExecuteChanged"/> es un evento propio que los consumidores
/// disparan llamando <see cref="RaiseCanExecuteChanged"/> explícitamente (los
/// setters de los VM ya lo hacen). La firma pública se mantiene idéntica para no
/// tocar los ~20 call-sites. <c>ICommand</c> vive en el BCL
/// (<c>System.Windows.Input</c>), disponible en net8.0 sin WPF.
/// </para>
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)    => _execute();

    public event EventHandler? CanExecuteChanged;

    /// <summary>Notifica a la UI que reevalúe <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => RaiseOnUiThread(CanExecuteChanged);

    internal static void RaiseOnUiThread(EventHandler? handler)
    {
        if (handler is null) return;
        if (Dispatcher.UIThread.CheckAccess())
            handler(null, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => handler(null, EventArgs.Empty));
    }
}

/// <summary>
/// Variante genérica de <see cref="RelayCommand"/> que recibe un parámetro
/// tipado al ejecutar — útil para bindings tipo <c>Command="..." CommandParameter="..."</c>.
/// </summary>
public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        var typed = parameter is T t ? t : default;
        return _canExecute?.Invoke(typed) ?? true;
    }

    public void Execute(object? parameter)
    {
        var typed = parameter is T t ? t : default;
        _execute(typed);
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged() => RelayCommand.RaiseOnUiThread(CanExecuteChanged);
}

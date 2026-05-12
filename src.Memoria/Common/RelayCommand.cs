using System;
using System.Windows.Input;

namespace MemoriaPlus.Common;

/// <summary>
/// <see cref="ICommand"/> minimalista para bindings MVVM. Empuja una <see cref="Action"/>
/// al click de un botón, con un opcional <see cref="Func{Boolean}"/> para
/// <see cref="ICommand.CanExecute"/>.
///
/// <para>
/// Mantiene la dependencia chica — no necesitamos CommunityToolkit.Mvvm
/// para el primer flujo de captura. Si la app crece (validaciones, async, etc.)
/// se puede migrar a CommunityToolkit.Mvvm.Input.RelayCommand sin cambiar la
/// firma de los consumidores.
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

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>Notifica a WPF que reevalúe <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// Variante genérica de <see cref="RelayCommand"/> que recibe un parámetro
/// tipado al ejecutar — útil para bindings tipo <c>Command="..." CommandParameter="..."</c>
/// donde el parámetro tiene que llegar al handler.
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

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

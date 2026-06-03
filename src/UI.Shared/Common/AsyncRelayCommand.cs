using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Shared.UI.Common;

/// <summary>
/// Comando MVVM para handlers asíncronos. Necesario en el port a Avalonia porque
/// los file pickers / clipboard de Avalonia son async (TopLevel.StorageProvider),
/// a diferencia de los diálogos síncronos de WPF (Microsoft.Win32). Mientras la
/// tarea corre, <see cref="CanExecute"/> devuelve false (guard de reentrancia) y
/// el botón queda deshabilitado.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
        => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try { await _execute(); }
        finally { _isExecuting = false; RaiseCanExecuteChanged(); }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => RelayCommand.RaiseOnUiThread(CanExecuteChanged);
}

/// <summary>Variante genérica de <see cref="AsyncRelayCommand"/> con parámetro tipado.</summary>
public sealed class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        var typed = parameter is T t ? t : default;
        return !_isExecuting && (_canExecute?.Invoke(typed) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        var typed = parameter is T t ? t : default;
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try { await _execute(typed); }
        finally { _isExecuting = false; RaiseCanExecuteChanged(); }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => RelayCommand.RaiseOnUiThread(CanExecuteChanged);
}

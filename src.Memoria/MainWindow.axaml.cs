using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LosasPlus.Persistence;
using MemoriaPlus.Services;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus;

public partial class MainWindow : Window
{
    /// <summary>
    /// True una vez que <see cref="OnClosing"/> ya resolvió la confirmación de
    /// cambios sin guardar y autorizó el cierre. Evita la re-entrada cuando
    /// llamamos <see cref="Window.Close"/> programáticamente.
    /// </summary>
    private bool _allowClose;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // Registra esta ventana como el TopLevel activo para los servicios de
        // diálogo/portapapeles (AppServices) consumidos por los ViewModels.
        AppServices.TopLevelAccessor = () => this;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
    }

    /// <summary>
    /// Intercepta el cierre para no perder trabajo sin guardar (Fase A — pérdida
    /// de datos). Si el proyecto está sucio, cancela el cierre, pregunta si
    /// guardar / descartar y solo cierra cuando el usuario decidió.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;                       // ya autorizado: dejar cerrar
        if (DataContext is not MainViewModel vm || !vm.IsDirty) return;

        e.Cancel = true;                               // detener este cierre

        // 3 estados: Guardar antes de cerrar / Descartar y cerrar / Cancelar
        // (quedarse). CRÍTICO (Fase A): descartar el diálogo (Escape / X) devuelve
        // Cancelar = quedarse, nunca descarta el trabajo en silencio.
        var r = await AppServices.MessageBox.ConfirmarGuardarDescartarCancelarAsync(
            "Cambios sin guardar",
            "¿Querés guardar los cambios antes de cerrar?");
        if (r == ResultadoDescarte.Cancelar) return;   // quedarse abierto
        if (r == ResultadoDescarte.Guardar)
        {
            var ok = await vm.GuardarAsync();
            if (!ok) return;                           // guardado falló/cancelado: no cerrar
        }

        _allowClose = true;
        Close();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        AplicarAtajos(AtajosService.Load());
        AtajosService.AtajosCambiados += OnAtajosCambiados;
    }

    private void OnClosed(object? sender, EventArgs e)
        => AtajosService.AtajosCambiados -= OnAtajosCambiados;

    private void OnAtajosCambiados(object? sender, AtajosConfig cfg)
        => Dispatcher.UIThread.Post(() => AplicarAtajos(cfg));

    /// <summary>
    /// Repuebla <see cref="InputElement.KeyBindings"/> a partir del config,
    /// mapeando cada id de atajo a su Command del MainViewModel. Port a Avalonia:
    /// WPF Window.InputBindings/KeyBinding(command,key,mods) → Window.KeyBindings
    /// con <see cref="KeyGesture"/>.
    /// </summary>
    private void AplicarAtajos(AtajosConfig cfg)
    {
        if (DataContext is not MainViewModel vm) return;
        KeyBindings.Clear();

        var mapa = new Dictionary<string, ICommand?>
        {
            { AtajoIds.NuevoProyecto, vm.NuevoProyectoCommand },
            { AtajoIds.Abrir,         vm.AbrirProyectoCommand },
            { AtajoIds.Guardar,       vm.GuardarBorradorCommand },
            { AtajoIds.GuardarComo,   vm.GuardarComoCommand },
            { AtajoIds.Generar,       vm.GenerarMemoriaCommand },
            { AtajoIds.AgregarLosa,   vm.AgregarLosaCommand },
            { AtajoIds.Busqueda,      vm.IrABusquedaCommand },
        };

        foreach (var (id, command) in mapa)
        {
            if (command is null) continue;
            var gestureStr = cfg.Get(id);
            if (string.IsNullOrWhiteSpace(gestureStr)) continue;

            if (TryParseGesture(gestureStr, out var key, out var mods))
                KeyBindings.Add(new KeyBinding { Command = command, Gesture = new KeyGesture(key, mods) });
        }
    }

    /// <summary>
    /// Parsea "Ctrl+Shift+S" → <see cref="Key"/> + <see cref="KeyModifiers"/>.
    /// Tokens por '+', modificadores case-insensitive; dígitos 0..9 → Key.D0..D9.
    /// </summary>
    private static bool TryParseGesture(string gesture, out Key key, out KeyModifiers mods)
    {
        key = Key.None;
        mods = KeyModifiers.None;
        var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= KeyModifiers.Control; break;
                case "shift":   mods |= KeyModifiers.Shift;   break;
                case "alt":     mods |= KeyModifiers.Alt;     break;
                case "win":
                case "windows": mods |= KeyModifiers.Meta;    break;
                default: return false;
            }
        }

        var keyToken = tokens[^1];
        if (keyToken.Length == 1 && char.IsDigit(keyToken[0]))
            keyToken = "D" + keyToken;

        return Enum.TryParse(keyToken, ignoreCase: true, out key) && key != Key.None;
    }
}

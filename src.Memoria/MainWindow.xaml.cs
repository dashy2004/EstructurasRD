using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using LosasPlus.Persistence;
using MemoriaPlus.ViewModels;

namespace MemoriaPlus;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Carga inicial de los atajos personalizados.
        AplicarAtajos(AtajosService.Load());
        // Recompone las InputBindings cada vez que el usuario guarda en
        // Configuración → Atajos (vía AtajosService.AtajosCambiados).
        AtajosService.AtajosCambiados += OnAtajosCambiados;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        AtajosService.AtajosCambiados -= OnAtajosCambiados;
    }

    private void OnAtajosCambiados(object? sender, AtajosConfig cfg) =>
        Dispatcher.BeginInvoke(new Action(() => AplicarAtajos(cfg)));

    /// <summary>
    /// Vacía <see cref="Window.InputBindings"/> y la repuebla a partir del
    /// config dado, mapeando cada id de atajo a su Command del MainViewModel
    /// vía nombre de propiedad. Gestures inválidos se omiten silenciosamente
    /// — no queremos crashear la app por una entrada malformada del JSON.
    /// </summary>
    private void AplicarAtajos(AtajosConfig cfg)
    {
        if (DataContext is not MainViewModel vm) return;
        InputBindings.Clear();

        // Mapeo id de atajo → command del VM (por instancia, no por nombre).
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
                InputBindings.Add(new KeyBinding(command, key, mods));
        }
    }

    /// <summary>
    /// Parsea un gesture string ("Ctrl+Shift+S") a un <see cref="Key"/> +
    /// <see cref="ModifierKeys"/>. Tokens separados por '+', case-insensitive
    /// en modificadores. Los dígitos 0..9 se traducen al Key.D0..D9
    /// correspondiente porque <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// no acepta "5" directamente.
    /// </summary>
    private static bool TryParseGesture(string gesture, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return false;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var t = tokens[i].ToLowerInvariant();
            switch (t)
            {
                case "ctrl":
                case "control": mods |= ModifierKeys.Control; break;
                case "shift":   mods |= ModifierKeys.Shift;   break;
                case "alt":     mods |= ModifierKeys.Alt;     break;
                case "win":
                case "windows": mods |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var keyToken = tokens[^1];
        if (keyToken.Length == 1 && char.IsDigit(keyToken[0]))
            keyToken = "D" + keyToken;  // "5" → Key.D5

        return Enum.TryParse<Key>(keyToken, ignoreCase: true, out key) && key != Key.None;
    }
}

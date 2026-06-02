using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using LosasPlus.Persistence;

namespace LosasPlus;

public partial class App : Application
{
    public enum ThemeKind { Dark, Light, Precision }

    private static ThemeKind _current = ThemeKind.Precision;
    public static ThemeKind CurrentTheme => _current;

    /// <summary>Disparado al cambiar el tema, para vistas que cachean recursos.</summary>
    public static event Action<ThemeKind>? ThemeChanged;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var saved = LoadThemePreference();
        if (saved != _current) SetTheme(saved);
        try { AplicarApariencia(AparienciaService.Load()); } catch { /* primer arranque */ }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Reemplaza el primer ResourceDictionary fusionado por el tema elegido.</summary>
    public static void SetTheme(ThemeKind theme)
    {
        var name = theme switch
        {
            ThemeKind.Light => "ThemeLight",
            ThemeKind.Dark  => "ThemeDark",
            _               => "ThemePrecision",
        };
        var uri = new Uri($"avares://LosasPlus/Resources/{name}.axaml");
        var dict = new ResourceInclude(uri) { Source = uri };

        if (Current is { } app)
        {
            var merged = app.Resources.MergedDictionaries;
            if (merged.Count > 0) merged[0] = dict;
            else merged.Add(dict);
        }

        _current = theme;
        SaveThemePreference(theme);
        try { Services.ThemeCustomizer.AplicarOverridesPersistidos(); } catch { /* primer arranque */ }
        ThemeChanged?.Invoke(theme);
    }

    public static void ToggleTheme()
        => SetTheme(_current switch
        {
            ThemeKind.Precision => ThemeKind.Light,
            ThemeKind.Light     => ThemeKind.Dark,
            _                   => ThemeKind.Precision,
        });

    // ---------- Apariencia (perfil + tipografía + acento) ----------
    public static void AplicarApariencia(AparienciaConfig cfg)
    {
        if (cfg is null || Current is not { } app) return;

        var temaUpper = (cfg.Tema ?? "").Trim().ToUpperInvariant();
        if      (temaUpper is "CLARO"  or "LIGHT") SetTheme(ThemeKind.Light);
        else if (temaUpper is "OSCURO" or "DARK")  SetTheme(ThemeKind.Dark);

        if (!string.IsNullOrWhiteSpace(cfg.TipografiaDatos))
        {
            try { app.Resources["FontFamilyMono"] = new FontFamily(cfg.TipografiaDatos + ", Consolas, monospace"); }
            catch { /* familia inválida */ }
        }

        if (cfg.TieneColorAcentoCustom)
        {
            try
            {
                var brush = new SolidColorBrush(Color.Parse(cfg.ColorAcentoHex));
                app.Resources["Accent"]       = brush;
                app.Resources["AccentHi"]     = brush;
                app.Resources["PrimaryBrush"] = brush;
            }
            catch { /* hex inválido */ }
        }
    }

    // ---------- Persistencia en %AppData%\LosasPlus\theme.txt ----------
    private static string PrefsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LosasPlus");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "theme.txt");
    }

    private static void SaveThemePreference(ThemeKind t)
    {
        try { File.WriteAllText(PrefsPath(), t.ToString()); } catch { /* no crítico */ }
    }

    public static ThemeKind LoadThemePreference()
    {
        try
        {
            var p = PrefsPath();
            if (File.Exists(p) && Enum.TryParse<ThemeKind>(File.ReadAllText(p).Trim(), out var t))
                return t;
        }
        catch { /* ignore */ }
        return ThemeKind.Precision;
    }
}

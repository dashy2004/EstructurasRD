using System;
using System.IO;
using System.Windows;

namespace LosasPlus;

public partial class App : Application
{
    public enum ThemeKind { Dark, Light, Precision }

    private static ThemeKind _current = ThemeKind.Precision;
    public static ThemeKind CurrentTheme => _current;

    /// <summary>Evento disparado cuando cambia el tema, para que vistas que cachean recursos se actualicen.</summary>
    public static event Action<ThemeKind>? ThemeChanged;

    /// <summary>Aplica el tema reemplazando el primer ResourceDictionary fusionado.</summary>
    public static void SetTheme(ThemeKind theme)
    {
        var src = theme switch
        {
            ThemeKind.Light => "Resources/ThemeLight.xaml",
            ThemeKind.Dark  => "Resources/ThemeDark.xaml",
            _               => "Resources/ThemePrecision.xaml",
        };

        var newDict = new ResourceDictionary
        {
            Source = new Uri(src, UriKind.Relative),
        };

        var merged = Current.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = newDict;
        else merged.Add(newDict);

        _current = theme;
        SaveThemePreference(theme);

        // Re-aplicar overrides personalizados después de cambiar el ResourceDictionary base
        try { LosasPlus.Services.ThemeCustomizer.AplicarOverridesPersistidos(); }
        catch { /* primer arranque, sin overrides */ }

        ThemeChanged?.Invoke(theme);
    }

    /// <summary>Avanza cíclicamente: Precision → Light → Dark → Precision.</summary>
    public static void ToggleTheme()
        => SetTheme(_current switch
        {
            ThemeKind.Precision => ThemeKind.Light,
            ThemeKind.Light     => ThemeKind.Dark,
            _                   => ThemeKind.Precision,
        });

    // ---------- Persistencia simple en %AppData%\LosasPlus\theme.txt ----------

    private static string PrefsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LosasPlus");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "theme.txt");
    }

    private static void SaveThemePreference(ThemeKind t)
    {
        try { File.WriteAllText(PrefsPath(), t.ToString()); }
        catch { /* sin persistencia, no es crítico */ }
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Cargar preferencia guardada (sólo si difiere del default).
        var saved = LoadThemePreference();
        if (saved != _current) SetTheme(saved);
    }
}

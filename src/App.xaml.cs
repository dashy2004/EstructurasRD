using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using HelixToolkit.SharpDX.Utilities;
using LosasPlus.Persistence;

namespace LosasPlus;

public partial class App : Application
{
    /// <summary>
    /// Fuerza el uso de la GPU dedicada (NVIDIA) en laptops con gráficos
    /// duales (Optimus). Esta variable estática se inicializa al cargar la
    /// clase <c>App</c> — antes de que DirectX 11 se enganche al
    /// <c>EffectsManager</c> de cualquier <c>Viewport3DX</c>. El selector
    /// automático del manager por defecto no garantiza elegir la GPU
    /// dedicada, así que esta export-table de NVAPI registrada al inicio del
    /// proceso resuelve el problema documentado en HelixToolkit Issue
    /// "Auto adapter selection does not guarantee Nvidia GPU".
    /// Añadido en Fase 3D-I1 (Plan Maestro de Expansión 3D).
    /// </summary>
    private static readonly NVOptimusEnabler _nvOptimusEnabler = new();

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

        // Aplicar AparienciaConfig persistido (tema + tipografía + color acento).
        // Hasta hoy (commit 50c997d) este AparienciaConfig sólo persistía JSON
        // y no aplicaba al runtime — la pestaña Apariencia se veía "muerta".
        try { AplicarApariencia(AparienciaService.Load()); }
        catch { /* primer arranque sin config */ }
    }

    // =====================================================================
    // APARIENCIA — wiring entre AparienciaConfig y el runtime
    // =====================================================================

    /// <summary>
    /// Aplica un <see cref="AparienciaConfig"/> al ResourceDictionary global
    /// en vivo. Esto cubre:
    /// <list type="bullet">
    ///   <item><b>Tema</b>: "Claro" → <c>ThemeKind.Light</c>, "Oscuro" →
    ///         <c>ThemeKind.Dark</c>. Empty/otro → no toca el tema actual.</item>
    ///   <item><b>Tipografía</b>: muta <c>FontFamilyMono</c> (que las tablas
    ///         de datos usan) según <see cref="AparienciaConfig.TipografiaDatos"/>.</item>
    ///   <item><b>Color de acento</b>: si <see cref="AparienciaConfig.TieneColorAcentoCustom"/>
    ///         es true, muta los brushes <c>Accent</c>, <c>AccentHi</c> (paleta
    ///         legacy) y <c>PrimaryBrush</c> (paleta Material 3 del shared).</item>
    /// </list>
    /// <para>
    /// La <b>densidad</b> (RowHeight) no es aplicable en vivo desde aquí
    /// porque cada DataGrid bindea su RowHeight individualmente; la
    /// <see cref="AparienciaConfig.RowHeight"/> está expuesta para que vistas
    /// nuevas la consuman a futuro.
    /// </para>
    /// </summary>
    public static void AplicarApariencia(AparienciaConfig cfg)
    {
        if (cfg is null) return;
        if (Current is null) return;

        // 1. Tema base (Claro / Oscuro). Mantiene Precision si vacío.
        var temaUpper = (cfg.Tema ?? "").Trim().ToUpperInvariant();
        if      (temaUpper == "CLARO"  || temaUpper == "LIGHT") SetTheme(ThemeKind.Light);
        else if (temaUpper == "OSCURO" || temaUpper == "DARK")  SetTheme(ThemeKind.Dark);
        // (Cualquier otro valor: no tocar el tema actual.)

        // 2. Tipografía monoespaciada de tablas de datos.
        if (!string.IsNullOrWhiteSpace(cfg.TipografiaDatos))
        {
            try
            {
                var ff = new FontFamily(cfg.TipografiaDatos + ", Consolas, monospace");
                Current.Resources["FontFamilyMono"] = ff;
            }
            catch { /* familia inválida — ignorar */ }
        }

        // 3. Color de acento — si hay override válido, propagarlo a las
        // claves de las dos paletas (legacy LosasPlus + Material 3 shared).
        if (cfg.TieneColorAcentoCustom)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(cfg.ColorAcentoHex);
                AplicarColorAcento(color);
            }
            catch { /* hex inválido — ignorar */ }
        }
    }

    /// <summary>Setea las claves de color de acento en ambas paletas (legacy + M3).</summary>
    private static void AplicarColorAcento(Color color)
    {
        if (Current is null) return;
        var brush = new SolidColorBrush(color);
        // Paleta legacy (LosasPlus Resources/Theme*.xaml)
        Current.Resources["Accent"]   = brush;
        Current.Resources["AccentHi"] = brush;
        // Paleta Material 3 (MemoriaPlus.UI.Shared/Resources/Theme.xaml)
        Current.Resources["PrimaryBrush"] = brush;
    }
}

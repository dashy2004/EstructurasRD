using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace LosasPlus.Services;

/// <summary>
/// Personaliza al vuelo cualquier brush del theme activo y persiste la config en
/// <c>%AppData%\LosasPlus\theme_overrides.json</c>. Port a Avalonia: System.Windows.Media
/// → Avalonia.Media; Application.Current.Resources con TryGetResource; sin Freeze.
/// </summary>
public static class ThemeCustomizer
{
    public sealed record TokenDef(string Key, string Categoria, string Descripcion);

    public static readonly IReadOnlyList<TokenDef> EditableTokens = new[]
    {
        new TokenDef("BgPrimary",   "Fondos",   "Fondo principal de la aplicación"),
        new TokenDef("BgSecondary", "Fondos",   "Sidebar y paneles secundarios"),
        new TokenDef("BgTertiary",  "Fondos",   "Hover/active de tabs, headers de tabla"),
        new TokenDef("BgInput",     "Fondos",   "Campos de entrada, cards"),
        new TokenDef("FgPrimary",   "Texto",    "Texto principal"),
        new TokenDef("FgSecondary", "Texto",    "Texto secundario (no activo)"),
        new TokenDef("FgMuted",     "Texto",    "Texto atenuado / hints"),
        new TokenDef("FgOnAccent",  "Texto",    "Texto sobre fondo Accent (botones primarios)"),
        new TokenDef("Accent",      "Acento",   "Color principal de marca / botones primarios / activo"),
        new TokenDef("AccentHi",    "Acento",   "Variante hover/highlight del Accent"),
        new TokenDef("ActiveBar",   "Acento",   "Barra del tab activo (border-left)"),
        new TokenDef("Border",      "Bordes",   "Bordes de cards/inputs/tablas"),
        new TokenDef("Selection",   "Bordes",   "Selección de celdas/items"),
        new TokenDef("Warn",        "Estados",  "Color de warning"),
        new TokenDef("Err",         "Estados",  "Color de error"),
        new TokenDef("Ok",          "Estados",  "Color OK / status connected"),
        new TokenDef("RowAlt",      "Estados",  "Fondo de fila alternada (zebra-stripe)"),
    };

    public static Color? GetColor(string key)
    {
        if (Application.Current is { } app
            && app.Resources.TryGetResource(key, null, out var r)
            && r is ISolidColorBrush b)
            return b.Color;
        return null;
    }

    public static void SetColor(string key, Color color)
    {
        if (Application.Current is not { } app) return;
        if (app.Resources.TryGetResource(key, null, out var r) && r is SolidColorBrush b)
            b.Color = color;
        else
            app.Resources[key] = new SolidColorBrush(color);
    }

    public static void RestaurarDefaults()
    {
        App.SetTheme(App.CurrentTheme);
        try { if (File.Exists(PrefsPath())) File.Delete(PrefsPath()); }
        catch { /* ignore */ }
    }

    public static void AplicarOverridesPersistidos()
    {
        foreach (var kv in LoadOverrides())
        {
            try { SetColor(kv.Key, Color.Parse(kv.Value)); }
            catch { /* token inválido */ }
        }
    }

    public static void GuardarOverrides()
    {
        var dict = new Dictionary<string, string>();
        foreach (var t in EditableTokens)
        {
            var c = GetColor(t.Key);
            if (c.HasValue) dict[t.Key] = ColorToHex(c.Value);
        }
        try
        {
            File.WriteAllText(PrefsPath(),
                JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }

    private static Dictionary<string, string> LoadOverrides()
    {
        try
        {
            var p = PrefsPath();
            if (!File.Exists(p)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) ?? new();
        }
        catch { return new(); }
    }

    public static string ColorToHex(Color c)
        => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color HexToColor(string hex)
    {
        try { return Color.Parse(hex); }
        catch { return Colors.Black; }
    }

    private static string PrefsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LosasPlus");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "theme_overrides.json");
    }
}

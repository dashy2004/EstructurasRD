using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace LosasPlus.Services;

/// <summary>
/// Permite personalizar al vuelo cualquier brush del theme activo y persiste la
/// configuración en <c>%AppData%\LosasPlus\theme_overrides.json</c>.
///
/// La lista <see cref="EditableTokens"/> declara qué claves del ResourceDictionary
/// se exponen en la UI de Configuración → Apariencia.
/// </summary>
public static class ThemeCustomizer
{
    /// <summary>Catálogo de tokens del theme que el usuario puede editar.</summary>
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

        new TokenDef("Warn",        "Estados",  "Color de warning (aspecto fuera de rango, etc.)"),
        new TokenDef("Err",         "Estados",  "Color de error (validación rota)"),
        new TokenDef("Ok",          "Estados",  "Color OK / status connected"),
        new TokenDef("RowAlt",      "Estados",  "Fondo de fila alternada (zebra-stripe)"),
    };

    /// <summary>Devuelve el color actual del token (consultando el ResourceDictionary activo).</summary>
    public static Color? GetColor(string key)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush b) return b.Color;
        return null;
    }

    /// <summary>Setea el color del token en runtime (modificando el SolidColorBrush in-place).</summary>
    public static void SetColor(string key, Color color)
    {
        if (Application.Current is null) return;
        if (Application.Current.Resources[key] is SolidColorBrush b)
        {
            // El brush puede estar congelado (Frozen). En tal caso reemplazamos.
            if (b.IsFrozen)
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
            }
            else
            {
                b.Color = color;
            }
        }
        else
        {
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }

    /// <summary>Restaura el theme actual recargando el ResourceDictionary base (descarta overrides).</summary>
    public static void RestaurarDefaults()
    {
        // Re-aplica el theme actual desde el archivo, lo cual recrea los brushes originales.
        App.SetTheme(App.CurrentTheme);
        // Borra los overrides persistidos
        try { if (File.Exists(PrefsPath())) File.Delete(PrefsPath()); }
        catch { /* ignore */ }
    }

    /// <summary>Aplica los overrides guardados después de cambiar de theme. Llamado desde App.SetTheme.</summary>
    public static void AplicarOverridesPersistidos()
    {
        var overrides = LoadOverrides();
        foreach (var kv in overrides)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(kv.Value);
                SetColor(kv.Key, c);
            }
            catch { /* token inválido, ignorar */ }
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
            var json = File.ReadAllText(p);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static string ColorToHex(Color c)
        => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color HexToColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
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

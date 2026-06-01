using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using LosasPlus.Validation;

namespace MemoriaPlus.Common;

/// <summary>
/// Converters para el formato condicional por severidad de la validación
/// normativa. En WPF esto se hacía con DataTrigger sobre <c>Severidad</c>; en
/// Avalonia se resuelve con value converters bindeados a Text/Foreground/etc.
/// </summary>
public static class ValidacionConverters
{
    /// <summary>Severidad → icono (⊘ error, ⚠ observación).</summary>
    public static readonly IValueConverter Icono =
        new FuncValueConverter<ValidationSeverity, string>(s => s == ValidationSeverity.Error ? "⊘" : "⚠");

    /// <summary>Severidad → brush (rojo error, ámbar observación).</summary>
    public static readonly IValueConverter Brush =
        new FuncValueConverter<ValidationSeverity, IBrush>(s =>
            s == ValidationSeverity.Error
                ? new SolidColorBrush(Color.Parse("#BA1A1A"))
                : new SolidColorBrush(Color.Parse("#C18A2C")));

    /// <summary>Severidad → etiqueta (CRÍTICO / OBSERVACIÓN).</summary>
    public static readonly IValueConverter Etiqueta =
        new FuncValueConverter<ValidationSeverity, string>(s => s == ValidationSeverity.Error ? "CRÍTICO" : "OBSERVACIÓN");

    /// <summary>Severidad → fondo del badge (~15% rojo / naranja).</summary>
    public static readonly IValueConverter BadgeFondo =
        new FuncValueConverter<ValidationSeverity, IBrush>(s =>
            s == ValidationSeverity.Error
                ? new SolidColorBrush(Color.Parse("#26D32F2F"))
                : new SolidColorBrush(Color.Parse("#26FFA000")));

    /// <summary>int 0 → true (para mostrar el empty state).</summary>
    public static readonly IValueConverter EsCero =
        new FuncValueConverter<int, bool>(n => n == 0);

    /// <summary>ColorIndicador (Error/Warning/otro) → fondo del chip de validación.</summary>
    public static readonly IValueConverter ChipFondo =
        new FuncValueConverter<object?, IBrush>(v => (v?.ToString()) switch
        {
            "Error"   => new SolidColorBrush(Color.Parse("#1FD32F2F")),
            "Warning" => new SolidColorBrush(Color.Parse("#1FFFA000")),
            _         => new SolidColorBrush(Color.Parse("#1F4CAF50")),
        });

    /// <summary>ColorIndicador → borde del chip de validación.</summary>
    public static readonly IValueConverter ChipBorde =
        new FuncValueConverter<object?, IBrush>(v => (v?.ToString()) switch
        {
            "Error"   => new SolidColorBrush(Color.Parse("#BA1A1A")),
            "Warning" => new SolidColorBrush(Color.Parse("#C18A2C")),
            _         => new SolidColorBrush(Color.Parse("#2D7A4F")),
        });
}

using System;
using System.Globalization;
using System.IO;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace MemoriaPlus.Common;

/// <summary>
/// Convierte un path absoluto en un <see cref="Bitmap"/> de Avalonia cargado
/// desde disco. Lee vía <see cref="File.OpenRead"/> y copia los bytes a memoria
/// (el stream se cierra), por lo que no bloquea el archivo: el usuario puede
/// mover o sobrescribir el PNG sin cerrar la app.
///
/// <para>
/// Si el path es null/vacío o el archivo no existe/está corrupto, devuelve null
/// (Image vacía — el caller oculta el control con un binding a IsVisible).
/// </para>
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    /// <summary>Instancia compartida para usar vía <c>{x:Static}</c>.</summary>
    public static readonly PathToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return new Bitmap(fs);
        }
        catch
        {
            // PNG corrupto o permisos — null muestra el empty state.
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>
/// Devuelve <c>true</c> cuando un string no es null y tiene al menos un caracter
/// no-whitespace. Usado para toggles de <c>IsVisible</c> sobre paths.
/// </summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

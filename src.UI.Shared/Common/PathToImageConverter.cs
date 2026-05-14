using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MemoriaPlus.Common;

/// <summary>
/// Convierte un path absoluto en un <see cref="BitmapImage"/> cargado desde
/// disco. Usa <see cref="BitmapCacheOption.OnLoad"/> + <see cref="BitmapCreateOptions.IgnoreImageCache"/>
/// para no bloquear el archivo (el usuario puede mover o sobrescribir el PNG
/// sin tener que cerrar la app).
///
/// <para>
/// Si el path es null/vacío o el archivo no existe, devuelve null (que en
/// WPF resulta en una Image vacía — el caller debe ocultar el control con
/// un DataTrigger si quiere mostrar un empty state).
/// </para>
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;          // copia bytes a memoria
            img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            img.UriSource = new Uri(path, UriKind.Absolute);
            img.EndInit();
            img.Freeze();                                        // safe para cross-thread
            return img;
        }
        catch
        {
            // PNG corrupto o permisos — devolver null muestra empty state via el DataTrigger.
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// Devuelve <c>true</c> cuando un string no es null y tiene al menos un
/// caracter no-whitespace. Usado para DataTriggers binarios sobre paths.
/// </summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

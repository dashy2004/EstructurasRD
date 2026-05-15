using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Resources;

namespace MemoriaPlus.Common;

/// <summary>
/// Convierte el <c>Codigo</c> de un <c>TipoLosa</c> (int) en un pack URI a su
/// archivo SVG embebido en <c>MemoriaPlus.UI.Shared</c>:
/// <c>pack://application:,,,/MemoriaPlus.UI.Shared;component/Resources/icons/tipo_NN.svg</c>.
///
/// <para>
/// Si el SVG no existe (por ejemplo un código no estándar), devuelve
/// <see cref="DependencyProperty.UnsetValue"/> para que el SvgViewbox quede
/// sin source y el fallback visual del card (rect + texto en XAML) tome el
/// control.
/// </para>
/// </summary>
public sealed class TipoCodigoToSvgUriConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int codigo || codigo <= 0)
            return DependencyProperty.UnsetValue;

        var uri = new Uri(
            $"pack://application:,,,/MemoriaPlus.UI.Shared;component/Resources/icons/tipo_{codigo}.svg",
            UriKind.Absolute);
        try
        {
            // Verificar que el SVG existe como Resource embebido antes de
            // devolver la URI. Application.GetResourceStream lanza si no.
            StreamResourceInfo? info = Application.GetResourceStream(uri);
            if (info is null) return DependencyProperty.UnsetValue;
            info.Stream.Close();
            return uri;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

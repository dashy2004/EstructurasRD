using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Resources;
using LosasPlus.Models;

namespace MemoriaPlus.Common;

/// <summary>
/// Convierte el <c>Codigo</c> de un <c>TipoLosa</c> (int) en un pack URI a su
/// archivo SVG embebido en <c>MemoriaPlus.UI.Shared</c>:
/// <c>pack://application:,,,/MemoriaPlus.UI.Shared;component/Resources/icons/tipo_NN.svg</c>.
///
/// <para>
/// Devuelve <see cref="DependencyProperty.UnsetValue"/> (SvgViewbox sin source,
/// fallback visual del card) cuando:
/// </para>
/// <list type="bullet">
///   <item>El código no es uno de los 23 tipos permitidos
///         (<see cref="TipoLosa.CodigosValidos"/>) — fail-fast: un tipo
///         inválido nunca debe mostrar un icono como si fuera legítimo.</item>
///   <item>El SVG del código válido no está embebido (no debería pasar — los
///         23 están cubiertos).</item>
/// </list>
/// </summary>
public sealed class TipoCodigoToSvgUriConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int codigo || codigo <= 0)
            return DependencyProperty.UnsetValue;

        // Fail-fast: un código fuera del catálogo de 23 no tiene icono.
        if (!TipoLosa.EsCodigoValido(codigo))
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

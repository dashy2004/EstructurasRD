using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using LosasPlus.Models;

namespace Shared.UI.Common;

/// <summary>
/// Convierte el <c>Codigo</c> de un <c>TipoLosa</c> (int) en un
/// <see cref="SvgImage"/> cargado desde el SVG embebido en
/// <c>MemoriaPlus.UI.Shared</c>:
/// <c>avares://MemoriaPlus.UI.Shared/Resources/icons/tipo_NN.svg</c>.
///
/// <para>
/// Port a Avalonia: el esquema <c>pack://</c> de WPF se reemplaza por
/// <c>avares://</c>, <c>Application.GetResourceStream</c> por
/// <see cref="AssetLoader.Exists"/>, y se devuelve un <see cref="SvgImage"/>
/// (enlazable a <c>Image.Source</c>) en vez de una URI. Para un código fuera del
/// catálogo de 23 tipos válidos devuelve <see cref="BindingOperations.DoNothing"/>
/// (fail-fast: un tipo inválido nunca muestra icono).
/// </para>
/// </summary>
public sealed class TipoCodigoToSvgUriConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int codigo || codigo <= 0)
            return BindingOperations.DoNothing;

        // Fail-fast: un código fuera del catálogo de 23 no tiene icono.
        if (!TipoLosa.EsCodigoValido(codigo))
            return BindingOperations.DoNothing;

        var uriStr = $"avares://MemoriaPlus.UI.Shared/Resources/icons/tipo_{codigo}.svg";
        try
        {
            var uri = new Uri(uriStr);
            if (!AssetLoader.Exists(uri)) return BindingOperations.DoNothing;
            return new SvgImage { Source = SvgSource.Load(uriStr, null) };
        }
        catch
        {
            return BindingOperations.DoNothing;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

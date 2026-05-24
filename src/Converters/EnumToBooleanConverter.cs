using System;
using System.Globalization;
using System.Windows.Data;

namespace LosasPlus.Converters;

/// <summary>
/// Conversor estándar para bindear <c>RadioButton.IsChecked</c> a un
/// valor de tipo <see cref="Enum"/> — Módulo 2 Parte C Fase 3D-II del
/// Plan Maestro. Permite a la toolbar del visor 3D conmutar
/// <c>HerramientaActiva</c> sin código en code-behind:
///
/// <code>
/// &lt;RadioButton IsChecked="{Binding HerramientaActiva,
///     Converter={StaticResource EnumToBoolean},
///     ConverterParameter=CrearColumna}"/&gt;
/// </code>
///
/// <para>
/// <b>Convert</b> retorna <c>true</c> sii el nombre del enum value
/// actual coincide con el <paramref name="parameter"/> de string.
/// <b>ConvertBack</b> retorna el enum value parseado desde
/// <paramref name="parameter"/> cuando el RadioButton es marcado;
/// si el RadioButton se desmarca (IsChecked=false), retorna
/// <see cref="Binding.DoNothing"/> para no sobrescribir el valor
/// actual con basura.
/// </para>
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string paramStr) return false;
        return value.ToString() == paramStr;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string paramStr)
            return Binding.DoNothing;
        try
        {
            return Enum.Parse(targetType, paramStr);
        }
        catch (ArgumentException)
        {
            // El paramStr no es un nombre válido del enum target — el
            // binding queda inalterado en lugar de tirar excepción
            // (defensiva ante typos en XAML).
            return Binding.DoNothing;
        }
    }
}

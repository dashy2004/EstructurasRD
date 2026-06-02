using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace MemoriaPlus.Common;

/// <summary>
/// Converter para enlazar un <c>RadioButton.IsChecked</c> a una propiedad enum
/// del ViewModel (mismo uso que en WPF, ahora con la firma de Avalonia).
///
/// <para>
/// <see cref="Convert"/> devuelve <c>true</c> sii el valor del enum coincide con
/// el parámetro (comparación por nombre, ignora case). <see cref="ConvertBack"/>
/// devuelve el enum parseado si <c>value</c> es true; al desmarcar devuelve
/// <see cref="BindingOperations.DoNothing"/> (desmarcar es efecto colateral de
/// marcar otro radio del grupo, no un evento a propagar).
/// </para>
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    /// <summary>Instancia compartida para usar vía <c>{x:Static}</c> sin depender
    /// de que la app declare el converter como recurso.</summary>
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null && targetType.IsEnum)
        {
            try { return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true); }
            catch { return BindingOperations.DoNothing; }
        }
        return BindingOperations.DoNothing;
    }
}

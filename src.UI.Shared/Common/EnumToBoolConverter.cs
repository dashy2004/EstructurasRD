using System;
using System.Globalization;
using System.Windows.Data;

namespace MemoriaPlus.Common;

/// <summary>
/// Converter idiomático de WPF para enlazar un <c>RadioButton.IsChecked</c> a
/// una propiedad enum del ViewModel.
///
/// <para>
/// Uso típico:
/// </para>
/// <code lang="xaml">
/// &lt;RadioButton GroupName="SidebarNav"
///              IsChecked="{Binding ModoActivo,
///                                  Converter={StaticResource EnumToBoolConverter},
///                                  ConverterParameter=Calculos}"/&gt;
/// </code>
///
/// <para>
/// <see cref="Convert"/> devuelve <c>true</c> sii el valor del enum coincide con
/// el parámetro (comparación por nombre, ignora case).
/// </para>
///
/// <para>
/// <see cref="ConvertBack"/> devuelve el valor del enum parseado del parámetro
/// si <c>value</c> es <c>true</c>; cuando se desmarca, <see cref="Binding.DoNothing"/>
/// — porque desmarcar un RadioButton dentro de un grupo es solo el efecto colateral
/// de marcar otro, no un evento que debamos propagar.
/// </para>
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is not null && targetType.IsEnum)
        {
            try { return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true); }
            catch { return Binding.DoNothing; }
        }
        return Binding.DoNothing;
    }
}

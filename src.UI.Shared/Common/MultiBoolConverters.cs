using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace MemoriaPlus.Common;

/// <summary>
/// Multi-converters de booleanos para reemplazar los MultiDataTrigger de WPF
/// (que Avalonia no soporta). Se usan con <c>MultiBinding</c>.
/// </summary>
public static class MultiBoolConverters
{
    /// <summary>true sii TODOS los valores de entrada son false.</summary>
    public static readonly IMultiValueConverter NingunoTrue = new NingunoTrueConverter();

    private sealed class NingunoTrueConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
            => values.All(v => v is bool b && !b);
    }
}

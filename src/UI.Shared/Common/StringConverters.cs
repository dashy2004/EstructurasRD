using Avalonia.Data.Converters;

namespace MemoriaPlus.Common;

/// <summary>Converters de string a bool para toggles de IsVisible.</summary>
public static class StringConverters
{
    /// <summary>true sii el string NO es null ni vacío/whitespace.</summary>
    public static readonly IValueConverter NotNullOrEmpty =
        new FuncValueConverter<string?, bool>(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>true sii el string ES null o vacío/whitespace.</summary>
    public static readonly IValueConverter IsNullOrEmpty =
        new FuncValueConverter<string?, bool>(s => string.IsNullOrWhiteSpace(s));
}

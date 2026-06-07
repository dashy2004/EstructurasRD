using Avalonia.Controls;

namespace LosasPlus.Views;

/// <summary>
/// Editor de columnas (Fase J.10/D2): tabla editable de las columnas del
/// <c>NivelSeleccionado</c> + botones agregar/eliminar (ahora bindeados a los
/// comandos del VM <c>AgregarCommand</c>/<c>EliminarCommand</c>, no a handlers
/// de code-behind).
/// </summary>
public partial class ColumnasEditorView : UserControl
{
    public ColumnasEditorView()
    {
        InitializeComponent();
    }
}

public class NToKNConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is double d ? d / 1000.0 : value;
    public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new System.NotImplementedException();
}

public class BoolToStringConverter : Avalonia.Data.Converters.IValueConverter
{
    public string TrueText { get; set; } = "OK";
    public string FalseText { get; set; } = "ERROR";
    
    public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && b ? TrueText : FalseText;
    public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new System.NotImplementedException();
}

public class BoolToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public Avalonia.Media.IBrush TrueBrush { get; set; } = Avalonia.Media.Brushes.Transparent;
    public Avalonia.Media.IBrush FalseBrush { get; set; } = Avalonia.Media.Brushes.Transparent;

    public object? Convert(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && b ? TrueBrush : FalseBrush;
    public object? ConvertBack(object? value, System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new System.NotImplementedException();
}

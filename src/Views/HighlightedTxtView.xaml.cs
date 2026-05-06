using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LosasPlus.Services;
using static LosasPlus.Services.TxtLineClassifier;

namespace LosasPlus.Views;

/// <summary>
/// Visor read-only del .TXT con resaltado por línea según <see cref="TxtLineClassifier"/>.
/// Usa <see cref="DynamicResource"/> para los colores, así sigue al theme activo (dark/light)
/// — se subscribe a <see cref="App.ThemeChanged"/> para re-renderizar al cambiar de tema.
/// </summary>
public partial class HighlightedTxtView : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(HighlightedTxtView),
            new PropertyMetadata("", OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public HighlightedTxtView()
    {
        InitializeComponent();
        // Re-renderizar al cambiar el tema (los DynamicResource del FlowDocument no
        // siempre se refrescan correctamente porque los Run.Foreground los seteamos
        // como Brushes resueltos, no como DynamicResource).
        App.ThemeChanged += _ => Render(Text ?? "");
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((HighlightedTxtView)d).Render((string)(e.NewValue ?? ""));
    }

    private void Render(string raw)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = 12,
            PagePadding = new Thickness(0),
            LineHeight = double.NaN,
        };
        var para = new Paragraph { Margin = new Thickness(0) };

        var lines = (raw ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var run = new Run(line);
            ApplyStyle(run, line);
            para.Inlines.Add(run);
            if (i < lines.Length - 1) para.Inlines.Add(new LineBreak());
        }
        doc.Blocks.Add(para);
        Editor.Document = doc;
    }

    private void ApplyStyle(Run run, string line)
    {
        var kind = Classify(line);
        switch (kind)
        {
            case LineKind.Comment:
                run.Foreground = Brush("FgMuted");
                run.FontStyle = FontStyles.Italic;
                break;
            case LineKind.Separator:
                run.Foreground = Brush("FgMuted");
                break;
            case LineKind.SectionHeader:
                run.Foreground = Brush("AccentHi");
                run.FontWeight = FontWeights.Bold;
                break;
            case LineKind.ColumnHeader:
                run.Foreground = Brush("FgPrimary");
                run.FontWeight = FontWeights.SemiBold;
                break;
            case LineKind.Units:
                run.Foreground = Brush("FgMuted");
                run.FontStyle = FontStyles.Italic;
                break;
            case LineKind.DataRow:
                run.Foreground = Brush("FgSecondary");
                break;
            case LineKind.Title:
                run.Foreground = Brush("AccentHi");
                run.FontWeight = FontWeights.Bold;
                break;
            case LineKind.Meta:
                run.Foreground = Brush("FgMuted");
                break;
            case LineKind.Empty:
            case LineKind.Plain:
            default:
                run.Foreground = Brush("FgPrimary");
                break;
        }
    }

    /// <summary>Resuelve un brush del theme activo. Cae a un color por defecto si la clave no existe.</summary>
    private Brush Brush(string resourceKey)
    {
        if (TryFindResource(resourceKey) is Brush b) return b;
        return Brushes.Gray;
    }
}

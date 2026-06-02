using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using static LosasPlus.Services.TxtLineClassifier;

namespace LosasPlus.Views;

/// <summary>
/// Visor read-only del .TXT con resaltado por línea según TxtLineClassifier.
/// Port a Avalonia: FlowDocument/RichTextBox → SelectableTextBlock.Inlines;
/// DependencyProperty → StyledProperty. Sigue al theme activo (se re-renderiza
/// en App.ThemeChanged).
/// </summary>
public partial class HighlightedTxtView : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HighlightedTxtView, string>(nameof(Text), "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public HighlightedTxtView()
    {
        InitializeComponent();
        App.ThemeChanged += _ => Render(Text ?? "");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            Render(change.GetNewValue<string>() ?? "");
    }

    private void Render(string raw)
    {
        Editor.Inlines?.Clear();
        var inlines = Editor.Inlines;
        if (inlines is null) return;

        var lines = (raw ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var run = new Run(lines[i]);
            ApplyStyle(run, lines[i]);
            inlines.Add(run);
            if (i < lines.Length - 1) inlines.Add(new LineBreak());
        }
    }

    private void ApplyStyle(Run run, string line)
    {
        switch (Classify(line))
        {
            case LineKind.Comment:
                run.Foreground = Brush("FgMuted"); run.FontStyle = FontStyle.Italic; break;
            case LineKind.Separator:
                run.Foreground = Brush("FgMuted"); break;
            case LineKind.SectionHeader:
                run.Foreground = Brush("AccentHi"); run.FontWeight = FontWeight.Bold; break;
            case LineKind.ColumnHeader:
                run.Foreground = Brush("FgPrimary"); run.FontWeight = FontWeight.SemiBold; break;
            case LineKind.Units:
                run.Foreground = Brush("FgMuted"); run.FontStyle = FontStyle.Italic; break;
            case LineKind.DataRow:
                run.Foreground = Brush("FgSecondary"); break;
            case LineKind.Title:
                run.Foreground = Brush("AccentHi"); run.FontWeight = FontWeight.Bold; break;
            case LineKind.Meta:
                run.Foreground = Brush("FgMuted"); break;
            case LineKind.Empty:
            case LineKind.Plain:
            default:
                run.Foreground = Brush("FgPrimary"); break;
        }
    }

    private IBrush Brush(string resourceKey)
    {
        if (this.TryFindResource(resourceKey, out var v) && v is IBrush b) return b;
        return Brushes.Gray;
    }
}

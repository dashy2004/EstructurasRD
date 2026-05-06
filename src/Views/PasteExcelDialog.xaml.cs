using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LosasPlus.Models;
using LosasPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Diálogo para pegar una tabla de Excel y convertirla en una lista de Losas.
/// </summary>
public partial class PasteExcelDialog : Window
{
    /// <summary>Resultado del parser tras "Aplicar".</summary>
    public List<Losa>? ResultLosas { get; private set; }
    /// <summary>True si el usuario eligió reemplazar; false si agregar.</summary>
    public bool ModoReemplazar { get; private set; } = true;

    public PasteExcelDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => PasteBox.Focus();
    }

    private void OnPasteFromClipboard(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText()) PasteBox.Text = Clipboard.GetText();
        else MessageBox.Show("El portapapeles está vacío o no contiene texto.", "Pegar", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnPasteChanged(object sender, TextChangedEventArgs e)
        => UpdatePreview();

    private void OnPreview(object sender, RoutedEventArgs e)
        => UpdatePreview();

    private ExcelClipboardParser.Defaults BuildDefaults()
    {
        var inv = CultureInfo.InvariantCulture;
        return new ExcelClipboardParser.Defaults
        {
            Tipo = int.TryParse(TipoBox.Text.Trim(), NumberStyles.Integer, inv, out var t) ? t : 10,
            EspesorM = double.TryParse(EspesorBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, inv, out var h) ? h : 0.12,
            RecM = double.TryParse(RecBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, inv, out var r) ? r : 0.02,
            CargaTonM2 = double.TryParse(CargaBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, inv, out var w) ? w : 0.40,
            EspesorEnCm = EspesorCmBox.IsChecked == true,
        };
    }

    private void UpdatePreview()
    {
        var defaults = BuildDefaults();
        var res = ExcelClipboardParser.Parse(PasteBox.Text, defaults);

        // Bind preview directly with anonymous-style projection
        PreviewGrid.ItemsSource = res.Losas.Select(l => new
        {
            l.Id,
            Tipo = l.Tipo,
            Carga = l.Carga,
            H = l.Espesor,
            l.Lx,
            l.Ly,
            Rec = l.Rec,
            Aspecto = l.Aspecto,
        }).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{res.Losas.Count} losa(s) detectadas");
        if (res.LineasIgnoradas > 0) sb.AppendLine($"{res.LineasIgnoradas} línea(s) ignoradas");
        foreach (var w in res.Warnings.Take(8)) sb.AppendLine("• " + w);
        if (res.Warnings.Count > 8) sb.AppendLine($"... y {res.Warnings.Count - 8} más");
        StatusText.Text = sb.ToString();
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var defaults = BuildDefaults();
        var res = ExcelClipboardParser.Parse(PasteBox.Text, defaults);
        if (res.Losas.Count == 0)
        {
            MessageBox.Show("No se detectó ninguna losa válida. Revisá el formato y volvé a procesar.",
                "Pegar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ResultLosas = res.Losas;
        ModoReemplazar = ModoBox.SelectedIndex == 0;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

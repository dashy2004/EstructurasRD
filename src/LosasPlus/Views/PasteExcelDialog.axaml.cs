using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LosasPlus.Models;
using LosasPlus.Services;
using Shared.UI.Services;

namespace LosasPlus.Views;

/// <summary>
/// Diálogo para pegar una tabla de Excel y convertirla en una lista de Losas.
/// Port a Avalonia: Clipboard async vía TopLevel; MessageBox→AppServices;
/// DialogResult→Close(bool) (leído con <c>await ShowDialog&lt;bool&gt;</c>).
/// </summary>
public partial class PasteExcelDialog : Window
{
    public List<Losa>? ResultLosas { get; private set; }
    public bool ModoReemplazar { get; private set; } = true;

    public PasteExcelDialog()
    {
        InitializeComponent();
        Loaded += (_, __) => PasteBox.Focus();
    }

    private async void OnPasteFromClipboard(object? sender, RoutedEventArgs e)
    {
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        // CS0618: GetTextAsync sigue siendo la vía soportada en Avalonia 11.3
        // (la extensión TryGetTextAsync es más nueva). Se suprime el aviso.
#pragma warning disable CS0618
        var text = cb is null ? null : await cb.GetTextAsync();
#pragma warning restore CS0618
        if (!string.IsNullOrEmpty(text)) PasteBox.Text = text;
        else _ = AppServices.MessageBox.InfoAsync("Pegar", "El portapapeles está vacío o no contiene texto.");
    }

    private void OnPasteChanged(object? sender, TextChangedEventArgs e) => UpdatePreview();
    private void OnPreview(object? sender, RoutedEventArgs e) => UpdatePreview();

    private ExcelClipboardParser.Defaults BuildDefaults()
    {
        var inv = CultureInfo.InvariantCulture;
        return new ExcelClipboardParser.Defaults
        {
            Tipo = int.TryParse((TipoBox.Text ?? "").Trim(), NumberStyles.Integer, inv, out var t) ? t : 10,
            EspesorM = double.TryParse((EspesorBox.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, inv, out var h) ? h : 0.12,
            RecM = double.TryParse((RecBox.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, inv, out var r) ? r : 0.02,
            CargaTonM2 = double.TryParse((CargaBox.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, inv, out var w) ? w : 0.40,
            EspesorEnCm = EspesorCmBox.IsChecked == true,
        };
    }

    private void UpdatePreview()
    {
        var defaults = BuildDefaults();
        var res = ExcelClipboardParser.Parse(PasteBox.Text ?? "", defaults);

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

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var defaults = BuildDefaults();
        var res = ExcelClipboardParser.Parse(PasteBox.Text ?? "", defaults);
        if (res.Losas.Count == 0)
        {
            _ = AppServices.MessageBox.InfoAsync("Pegar",
                "No se detectó ninguna losa válida. Revisá el formato y volvé a procesar.");
            return;
        }
        ResultLosas = res.Losas;
        ModoReemplazar = ModoBox.SelectedIndex == 0;
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LosasPlus.ViewModels;
using MemoriaPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Ventana modal que muestra el desglose de la carga última (Wu) por losa tras
/// «Engine → Calcular carga última (Wu) desde geometría». Es de solo lectura:
/// los valores ya fueron escritos en el modelo (Losa.Carga) por
/// <see cref="MainViewModel.AplicarCargaUltimaConDesglose"/>; esta ventana solo
/// los presenta con sus unidades para que el ingeniero los revise.
/// </summary>
public partial class CargaUltimaWindow : Window
{
    private readonly IReadOnlyList<CargaUltimaFila> _filas;
    private readonly string _combinacion;

    // Constructor sin parámetros para el cargador XAML.
    public CargaUltimaWindow() : this(Array.Empty<CargaUltimaFila>()) { }

    /// <param name="filas">Desglose por losa ya calculado.</param>
    /// <param name="combinacion">
    /// Texto de la combinación última real del proyecto (p. ej.
    /// «Qu = 1.2·Qd + 1.6·Ql (ACI 318-05)»), tomado de
    /// <c>CargasGlobales.Factores</c>; no se hardcodea para no mentir si el
    /// proyecto cambia los factores.
    /// </param>
    public CargaUltimaWindow(IReadOnlyList<CargaUltimaFila> filas, string? combinacion = null)
    {
        InitializeComponent();
        _filas = filas ?? Array.Empty<CargaUltimaFila>();
        _combinacion = string.IsNullOrWhiteSpace(combinacion) ? "Qu = combinación última del proyecto" : combinacion!;
        FilasGrid.ItemsSource = _filas;

        int nSistemas = _filas.Select(f => f.Sistema).Distinct().Count();
        int nNiveles = _filas.Select(f => f.Nivel).Distinct().Count();
        double quMax = _filas.Count > 0 ? _filas.Max(f => f.Qu) : 0;
        ResumenLabel.Text = $"{_filas.Count} losa(s) en {nSistemas} sistema(s), {nNiveles} nivel(es) — Qu máx = " +
                            quMax.ToString("0.000", CultureInfo.InvariantCulture) + " t/m².";

        NotaLabel.Text = "Qmamp = peso total de los muros del sistema (ton) y Qmap su reparto sobre el área " +
                         "(t/m²); ambos son constantes por sistema. Qd, Ql y Qu en t/m² (Qd y Qu varían por el " +
                         $"espesor de cada losa). {_combinacion}. El valor Qu se escribió en cada losa (Losa.Carga) " +
                         "y alimenta la bajada de cargas, columnas y zapatas. Acción aditiva: no reemplaza el flujo de Losas.exe.";
    }

    private void OnCerrarClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopiarClick(object? sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CARGA ÚLTIMA (Wu) — desglose por losa");
        sb.AppendLine("Qmamp [ton, por sistema]; Qmap/Qd/Ql/Qu [t/m²]; " + _combinacion);
        sb.AppendLine(new string('-', 72));
        sb.AppendLine("Nivel\tSistema\tLosa\th(m)\tQmamp\tQmap\tQd\tQl\tQu");
        foreach (var f in _filas)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}\t{1}\t{2}\t{3:0.000}\t{4:0.00}\t{5:0.000}\t{6:0.000}\t{7:0.000}\t{8:0.000}",
                f.Nivel, f.Sistema, f.Losa, f.Espesor, f.Qmamp, f.Qmap, f.Qd, f.Ql, f.Qu));
        try
        {
            await AppServices.Clipboard.SetTextAsync(sb.ToString());
            await AppServices.MessageBox.InfoAsync("Carga última (Wu)", "Desglose copiado al portapapeles.");
        }
        catch (Exception ex)
        {
            await AppServices.MessageBox.InfoAsync("Carga última (Wu)", "No se pudo copiar: " + ex.Message);
        }
    }
}

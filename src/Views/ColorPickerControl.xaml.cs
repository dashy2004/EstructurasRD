using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LosasPlus.Services;

namespace LosasPlus.Views;

/// <summary>
/// Picker mínimo de color: swatch + HEX + 3 sliders R/G/B. Sin dependencias externas.
/// Cuando el usuario cambia algún componente, dispara <see cref="ColorChanged"/> con
/// el código del token y el color nuevo.
/// </summary>
public partial class ColorPickerControl : UserControl
{
    private bool _suspendUpdates;
    public string TokenKey { get; set; } = "";
    public Action<string, Color>? ColorChanged { get; set; }

    public ColorPickerControl()
    {
        InitializeComponent();
    }

    public void Bind(ThemeCustomizer.TokenDef token)
    {
        TokenKey = token.Key;
        LabelText.Text = $"{token.Key}\n{token.Descripcion}";
        var c = ThemeCustomizer.GetColor(token.Key) ?? Colors.Black;
        SetColor(c);
    }

    private void SetColor(Color c)
    {
        _suspendUpdates = true;
        Swatch.Background = new SolidColorBrush(c);
        RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
        RVal.Text = c.R.ToString(CultureInfo.InvariantCulture);
        GVal.Text = c.G.ToString(CultureInfo.InvariantCulture);
        BVal.Text = c.B.ToString(CultureInfo.InvariantCulture);
        HexBox.Text = ThemeCustomizer.ColorToHex(c);
        _suspendUpdates = false;
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suspendUpdates) return;
        var c = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
        _suspendUpdates = true;
        Swatch.Background = new SolidColorBrush(c);
        HexBox.Text = ThemeCustomizer.ColorToHex(c);
        RVal.Text = c.R.ToString(CultureInfo.InvariantCulture);
        GVal.Text = c.G.ToString(CultureInfo.InvariantCulture);
        BVal.Text = c.B.ToString(CultureInfo.InvariantCulture);
        _suspendUpdates = false;
        FireChange(c);
    }

    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_suspendUpdates) return;
        var hex = HexBox.Text.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            _suspendUpdates = true;
            Swatch.Background = new SolidColorBrush(c);
            RSlider.Value = c.R; GSlider.Value = c.G; BSlider.Value = c.B;
            RVal.Text = c.R.ToString(CultureInfo.InvariantCulture);
            GVal.Text = c.G.ToString(CultureInfo.InvariantCulture);
            BVal.Text = c.B.ToString(CultureInfo.InvariantCulture);
            _suspendUpdates = false;
            FireChange(c);
        }
        catch { /* HEX inválido en medio del typing — ignorar */ }
    }

    private void FireChange(Color c)
    {
        if (string.IsNullOrEmpty(TokenKey)) return;
        ThemeCustomizer.SetColor(TokenKey, c);
        ColorChanged?.Invoke(TokenKey, c);
    }
}

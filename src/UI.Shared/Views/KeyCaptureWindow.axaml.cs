using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace MemoriaPlus.Views;

/// <summary>
/// Modal de captura de combinación de teclas. El usuario presiona una secuencia,
/// la app calcula el "gesture string" (ej. "Ctrl+Shift+S") y lo devuelve por
/// <c>ShowDialog&lt;string?&gt;</c> (null si canceló). Esc cancela; Enter confirma.
///
/// <para>Port a Avalonia: WPF usaba PreviewKeyDown + DialogResult; aquí se usa
/// KeyDown en estrategia Tunnel y <see cref="Window.Close(object)"/> con el
/// resultado.</para>
/// </summary>
public partial class KeyCaptureWindow : Window
{
    /// <summary>Resultado de la captura, null si el usuario canceló.</summary>
    public string? GestureCapturado { get; private set; }

    private string _gestureBuffer = "";
    private readonly Button _botonConfirmar;
    private readonly TextBlock _preview;

    // Constructor sin parámetros requerido por el cargador XAML de Avalonia.
    public KeyCaptureWindow() : this("", null) { }

    public KeyCaptureWindow(string atajoEtiqueta, string? gestureActual = null)
    {
        AvaloniaXamlLoader.Load(this);
        _botonConfirmar = this.FindControl<Button>("BotonConfirmar")!;
        _preview = this.FindControl<TextBlock>("VistaPreviewGesture")!;
        this.FindControl<TextBlock>("EtiquetaContexto")!.Text = $"Atajo para: {atajoEtiqueta}";

        if (!string.IsNullOrWhiteSpace(gestureActual))
        {
            _gestureBuffer = gestureActual!;
            _preview.Text = gestureActual!;
            _botonConfirmar.IsEnabled = true;
        }

        // Captura en estrategia Tunnel para no perder Tab/Esc/Enter.
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Opened += (_, _) => Dispatcher.UIThread.Post(() => Focus());
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            GestureCapturado = null;
            Close(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _botonConfirmar.IsEnabled)
        {
            Confirmar();
            e.Handled = true;
            return;
        }

        // Ignorar modificadores puros.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl
                  or Key.LeftShift or Key.RightShift
                  or Key.LeftAlt or Key.RightAlt
                  or Key.LWin or Key.RWin)
        {
            return;
        }

        var gesture = ComponerGesture(e.KeyModifiers, e.Key);
        if (gesture is null) return;

        _gestureBuffer = gesture;
        _preview.Text = gesture;
        _botonConfirmar.IsEnabled = true;
        e.Handled = true;
    }

    /// <summary>
    /// Construye un gesture string ("Ctrl+Shift+S"): modificadores en orden
    /// Ctrl, Shift, Alt seguidos del key. null si el key no es representable.
    /// </summary>
    private static string? ComponerGesture(KeyModifiers mods, Key key)
    {
        var sb = new StringBuilder();
        if (mods.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (mods.HasFlag(KeyModifiers.Shift))   sb.Append("Shift+");
        if (mods.HasFlag(KeyModifiers.Alt))     sb.Append("Alt+");

        var keyName = key.ToString();
        if (string.IsNullOrEmpty(keyName) || keyName == "None") return null;
        // Normaliza D0..D9 → 0..9
        if (keyName.StartsWith("D") && keyName.Length == 2 && char.IsDigit(keyName[1]))
            keyName = keyName.Substring(1);
        sb.Append(keyName);
        return sb.ToString();
    }

    private void Confirmar_Click(object? sender, RoutedEventArgs e) => Confirmar();

    private void Cancelar_Click(object? sender, RoutedEventArgs e)
    {
        GestureCapturado = null;
        Close(null);
    }

    private void Confirmar()
    {
        if (string.IsNullOrEmpty(_gestureBuffer)) return;
        GestureCapturado = _gestureBuffer;
        Close(_gestureBuffer);
    }
}

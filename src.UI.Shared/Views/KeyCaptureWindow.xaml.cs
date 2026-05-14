using System.Text;
using System.Windows;
using System.Windows.Input;

namespace MemoriaPlus.Views;

/// <summary>
/// Modal de captura de combinación de teclas. El usuario presiona una
/// secuencia, la app calcula el "gesture string" (ej. "Ctrl+Shift+S") y lo
/// devuelve via <see cref="GestureCapturado"/>. Esc cancela; Enter confirma
/// (si hay captura). Si la combinación no incluye al menos una tecla "real"
/// (no-modificadora), no se habilita el botón Confirmar.
/// </summary>
public partial class KeyCaptureWindow : Window
{
    /// <summary>Resultado de la captura, null si el usuario canceló.</summary>
    public string? GestureCapturado { get; private set; }

    public KeyCaptureWindow(string atajoEtiqueta, string? gestureActual = null)
    {
        InitializeComponent();
        EtiquetaContexto.Text = $"Atajo para: {atajoEtiqueta}";
        if (!string.IsNullOrWhiteSpace(gestureActual))
        {
            _gestureBuffer = gestureActual;
            VistaPreviewGesture.Text = gestureActual;
            BotonConfirmar.IsEnabled = true;
        }
        // Captura PreviewKeyDown a nivel Window para no perder Tab/Esc.
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => Self.Focus();
    }

    private string _gestureBuffer = "";

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc: cancelar
        if (e.Key == Key.Escape)
        {
            GestureCapturado = null;
            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        // Enter: confirmar si hay gesture válido
        if (e.Key == Key.Enter && BotonConfirmar.IsEnabled)
        {
            Confirmar();
            e.Handled = true;
            return;
        }

        // Ignorar pulsaciones puras de modificadores (Ctrl/Shift/Alt sin tecla).
        if (e.Key is Key.LeftCtrl or Key.RightCtrl
                  or Key.LeftShift or Key.RightShift
                  or Key.LeftAlt or Key.RightAlt
                  or Key.System or Key.LWin or Key.RWin)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = ComponerGesture(Keyboard.Modifiers, key);
        if (gesture is null) return;

        _gestureBuffer = gesture;
        VistaPreviewGesture.Text = gesture;
        BotonConfirmar.IsEnabled = true;
        e.Handled = true;
    }

    /// <summary>
    /// Construye un gesture string al estilo WPF KeyGesture: modificadores
    /// alfabéticamente (Ctrl, Shift, Alt) seguidos del key (ej. "Ctrl+Shift+S").
    /// Devuelve null si el key no es representable (NoName / DeadCharProcessed
    /// y similares).
    /// </summary>
    private static string? ComponerGesture(ModifierKeys mods, Key key)
    {
        var sb = new StringBuilder();
        if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (mods.HasFlag(ModifierKeys.Shift))   sb.Append("Shift+");
        if (mods.HasFlag(ModifierKeys.Alt))     sb.Append("Alt+");

        var keyName = key.ToString();
        if (string.IsNullOrEmpty(keyName) || keyName == "None") return null;
        // Normaliza D0..D9 → 0..9
        if (keyName.StartsWith("D") && keyName.Length == 2 && char.IsDigit(keyName[1]))
            keyName = keyName.Substring(1);
        sb.Append(keyName);
        return sb.ToString();
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e) => Confirmar();
    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        GestureCapturado = null;
        DialogResult = false;
        Close();
    }

    private void Confirmar()
    {
        if (string.IsNullOrEmpty(_gestureBuffer)) return;
        GestureCapturado = _gestureBuffer;
        DialogResult = true;
        Close();
    }
}

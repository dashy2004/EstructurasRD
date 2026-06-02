namespace LosasPlus.Models.Cad;

/// <summary>
/// Punto 2D en el plano, expresado en <b>metros</b>. Struct liviano e inmutable.
///
/// <para>
/// Es un tipo <b>puro de dominio</b>: no referencia <c>System.Windows.Point</c>
/// ni ningún tipo de WPF. La capa de presentación es la responsable de
/// convertir <see cref="PuntoCad"/> a las primitivas gráficas de la UI.
/// </para>
/// </summary>
public readonly struct PuntoCad
{
    public PuntoCad(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Coordenada X (m).</summary>
    public double X { get; }

    /// <summary>Coordenada Y (m).</summary>
    public double Y { get; }

    /// <summary>Devuelve un punto con ambas coordenadas multiplicadas por <paramref name="factor"/>.</summary>
    public PuntoCad Escalar(double factor) => new(X * factor, Y * factor);

    public override string ToString() =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
}

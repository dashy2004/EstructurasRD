using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Cargas;

/// <summary>
/// Categoría de un <see cref="CasoCarga"/>. Determina cómo lo tratan las
/// combinaciones de carga y los módulos de diseño estructural.
/// </summary>
public enum TipoCasoCarga
{
    /// <summary>Carga muerta / permanente (D).</summary>
    Muerta,

    /// <summary>Carga viva de entrepiso (L).</summary>
    Viva,

    /// <summary>Carga viva de techo (Lr).</summary>
    Techo,

    /// <summary>Carga sísmica (E).</summary>
    Sismo,

    /// <summary>Carga de viento (W).</summary>
    Viento,

    /// <summary>Efectos de deformación impuesta — temperatura, retracción (T).</summary>
    Deformacion,
}

/// <summary>
/// Un caso de carga del proyecto: una acción estructural identificada por un
/// <see cref="Codigo"/> único (p. ej. «D», «L», «E») al que las
/// <see cref="CombinacionCarga"/> referencian por ese código.
///
/// <para>
/// Tipo <b>puro de dominio</b> (Fase 2 de la suite estructural) — no referencia
/// ningún tipo de WPF. Vive en <c>src.Core</c> y es reutilizable desde
/// cualquier consumidor .NET.
/// </para>
/// </summary>
public partial class CasoCarga : INotifyPropertyChanged
{
    private string _codigo = "";
    private string _nombre = "";
    private TipoCasoCarga _tipo = TipoCasoCarga.Muerta;

    /// <summary>
    /// Código corto y único del caso dentro del proyecto (p. ej. «D», «L»,
    /// «Lr», «E»). Es la clave por la que lo referencian los
    /// <see cref="TerminoCombinacion"/>.
    /// </summary>
    public string Codigo
    {
        get => _codigo;
        set { _codigo = value; OnPropertyChanged(); }
    }

    /// <summary>Nombre descriptivo del caso (p. ej. «Carga Muerta»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Categoría del caso — ver <see cref="TipoCasoCarga"/>.</summary>
    public TipoCasoCarga Tipo
    {
        get => _tipo;
        set { _tipo = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

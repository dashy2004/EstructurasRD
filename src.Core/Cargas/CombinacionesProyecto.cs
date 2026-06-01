using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LosasPlus.Cargas;

/// <summary>Uso de una <see cref="CombinacionCarga"/>.</summary>
public enum TipoCombinacion
{
    /// <summary>Combinación de servicio — esfuerzos admisibles (ASCE 7-05 §2.4).</summary>
    Servicio,

    /// <summary>Combinación última — diseño por resistencia (ASCE 7-05 §2.3).</summary>
    Ultima,
}

/// <summary>Norma de la que provienen los factores de combinación.</summary>
public enum NormaCombinaciones
{
    /// <summary>ASCE 7-05.</summary>
    Asce7_05,

    /// <summary>ASCE 7-22.</summary>
    Asce7_22,

    /// <summary>Reglamento dominicano R-026 (cargas mínimas).</summary>
    R026,

    /// <summary>Reglamento dominicano R-027 (análisis y diseño sísmico).</summary>
    R027,

    /// <summary>Conjunto definido manualmente por el usuario.</summary>
    Personalizada,
}

/// <summary>
/// Un término de una <see cref="CombinacionCarga"/>: un <see cref="CasoCarga"/>
/// —referenciado por su <see cref="CodigoCaso"/>— multiplicado por un
/// <see cref="Factor"/> de mayoración. La referencia es por <b>string</b> (no
/// por objeto) para que la serialización JSON no duplique el caso ni pierda la
/// identidad referencial.
/// </summary>
public partial class TerminoCombinacion : INotifyPropertyChanged
{
    private string _codigoCaso = "";
    private double _factor;

    /// <summary>Constructor sin parámetros — requerido por la deserialización JSON.</summary>
    public TerminoCombinacion() { }

    /// <summary>Constructor de conveniencia para construir combinaciones en código.</summary>
    public TerminoCombinacion(string codigoCaso, double factor)
    {
        _codigoCaso = codigoCaso;
        _factor = factor;
    }

    /// <summary>
    /// Código del <see cref="CasoCarga"/> al que aplica el factor — debe
    /// coincidir con un <see cref="CasoCarga.Codigo"/> de
    /// <see cref="CombinacionesProyecto.Casos"/>.
    /// </summary>
    public string CodigoCaso
    {
        get => _codigoCaso;
        set { _codigoCaso = value; OnPropertyChanged(); }
    }

    /// <summary>Factor de mayoración que multiplica al caso (p. ej. 1.2, 1.6).</summary>
    public double Factor
    {
        get => _factor;
        set { _factor = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Una combinación de carga: la suma lineal de casos factorizados
/// (<see cref="Terminos"/>) que el diseño estructural debe verificar. El
/// <see cref="Tipo"/> distingue las combinaciones de servicio de las últimas
/// — son estructuralmente idénticas, sólo cambia el uso.
/// </summary>
public partial class CombinacionCarga : INotifyPropertyChanged
{
    private string _nombre = "";
    private TipoCombinacion _tipo = TipoCombinacion.Ultima;

    /// <summary>Nombre legible de la combinación (p. ej. «1.2D + 1.6L + 0.5Lr»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Servicio o última — ver <see cref="TipoCombinacion"/>.</summary>
    public TipoCombinacion Tipo
    {
        get => _tipo;
        set { _tipo = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Términos caso·factor de la combinación. Colección get-only con
    /// inicializador — <c>ProyectoSerializer</c> la rellena vía <c>Populate</c>.
    /// </summary>
    public ObservableCollection<TerminoCombinacion> Terminos { get; } = new();

    /// <summary>
    /// Factor del caso <paramref name="codigoCaso"/> en esta combinación: el
    /// <see cref="TerminoCombinacion.Factor"/> del término con ese código, o
    /// <c>0</c> si no hay término. El <c>set</c> hace upsert (o borra el
    /// término si el factor es 0). Es el acceso que usa la matriz de factores
    /// de la UI; los indexers no los serializa System.Text.Json.
    /// </summary>
    public double this[string codigoCaso]
    {
        get => Terminos.FirstOrDefault(t => t.CodigoCaso == codigoCaso)?.Factor ?? 0.0;
        set
        {
            var termino = Terminos.FirstOrDefault(t => t.CodigoCaso == codigoCaso);
            if (value == 0.0)
            {
                if (termino is not null) Terminos.Remove(termino);
            }
            else if (termino is not null)
            {
                termino.Factor = value;
            }
            else
            {
                Terminos.Add(new TerminoCombinacion(codigoCaso, value));
            }
            OnPropertyChanged("Item[]");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Base de diseño de cargas del proyecto: el catálogo de <see cref="Casos"/> y
/// las <see cref="Combinaciones"/> que los mayoran. Es <b>transversal</b> a
/// todos los módulos de diseño (losas, vigas, columnas, zapatas) — por eso vive
/// a nivel de <c>Proyecto</c> y no de edificio o nivel (PROPUESTA §3.5).
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class CombinacionesProyecto : INotifyPropertyChanged
{
    private NormaCombinaciones _norma = NormaCombinaciones.Asce7_05;

    /// <summary>Norma de la que provienen los factores de combinación.</summary>
    public NormaCombinaciones Norma
    {
        get => _norma;
        set { _norma = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Casos de carga del proyecto. Colección get-only con inicializador vacío
    /// — la siembra se hace vía <see cref="SemillaPorDefecto"/>, nunca en el
    /// constructor (evita duplicar al deserializar con <c>Populate</c>).
    /// </summary>
    public ObservableCollection<CasoCarga> Casos { get; } = new();

    /// <summary>
    /// Combinaciones de carga del proyecto (servicio y últimas). Colección
    /// get-only con inicializador vacío — ver la nota en <see cref="Casos"/>.
    /// </summary>
    public ObservableCollection<CombinacionCarga> Combinaciones { get; } = new();

    /// <summary>
    /// Construye la base de cargas por defecto: los 4 casos fundamentales
    /// (D, L, Lr, E) y las 8 combinaciones estándar — 4 últimas
    /// (ASCE 7-05 §2.3.2) y 4 de servicio (ASCE 7-05 §2.4.1).
    /// </summary>
    /// <remarks>
    /// Esta iteración materializa únicamente el conjunto ASCE 7-05; las demás
    /// <see cref="NormaCombinaciones"/> se añadirán en iteraciones posteriores.
    /// El valor recibido se almacena en <see cref="Norma"/>.
    /// </remarks>
    public static CombinacionesProyecto SemillaPorDefecto(
        NormaCombinaciones norma = NormaCombinaciones.Asce7_05)
    {
        var cp = new CombinacionesProyecto { Norma = norma };

        // --- Casos de carga base ---
        cp.Casos.Add(new CasoCarga { Codigo = "D",  Nombre = "Carga Muerta",        Tipo = TipoCasoCarga.Muerta });
        cp.Casos.Add(new CasoCarga { Codigo = "L",  Nombre = "Carga Viva",          Tipo = TipoCasoCarga.Viva   });
        cp.Casos.Add(new CasoCarga { Codigo = "Lr", Nombre = "Carga Viva de Techo", Tipo = TipoCasoCarga.Techo  });
        cp.Casos.Add(new CasoCarga { Codigo = "E",  Nombre = "Sismo",               Tipo = TipoCasoCarga.Sismo  });

        // Helper local para construir una combinación con sus términos.
        static CombinacionCarga Combo(string nombre, TipoCombinacion tipo,
                                      params (string caso, double factor)[] terminos)
        {
            var c = new CombinacionCarga { Nombre = nombre, Tipo = tipo };
            foreach (var (caso, factor) in terminos)
                c.Terminos.Add(new TerminoCombinacion(caso, factor));
            return c;
        }

        // --- Combinaciones últimas — diseño por resistencia (ASCE 7-05 §2.3.2) ---
        cp.Combinaciones.Add(Combo("1.4D", TipoCombinacion.Ultima,
            ("D", 1.4)));
        cp.Combinaciones.Add(Combo("1.2D + 1.6L + 0.5Lr", TipoCombinacion.Ultima,
            ("D", 1.2), ("L", 1.6), ("Lr", 0.5)));
        cp.Combinaciones.Add(Combo("1.2D + 1.0E + 1.0L", TipoCombinacion.Ultima,
            ("D", 1.2), ("E", 1.0), ("L", 1.0)));
        cp.Combinaciones.Add(Combo("0.9D + 1.0E", TipoCombinacion.Ultima,
            ("D", 0.9), ("E", 1.0)));

        // --- Combinaciones de servicio — esfuerzos admisibles (ASCE 7-05 §2.4.1) ---
        cp.Combinaciones.Add(Combo("D", TipoCombinacion.Servicio,
            ("D", 1.0)));
        cp.Combinaciones.Add(Combo("D + L", TipoCombinacion.Servicio,
            ("D", 1.0), ("L", 1.0)));
        cp.Combinaciones.Add(Combo("D + 0.75L + 0.75Lr", TipoCombinacion.Servicio,
            ("D", 1.0), ("L", 0.75), ("Lr", 0.75)));
        cp.Combinaciones.Add(Combo("D + 0.7E", TipoCombinacion.Servicio,
            ("D", 1.0), ("E", 0.7)));

        return cp;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

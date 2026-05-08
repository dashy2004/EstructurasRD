using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace LosasPlus.Models;

/// <summary>
/// Configuración global de cargas para un <see cref="Proyecto"/> de Memoria Plus.
/// Estos valores aplican a todos los niveles del proyecto a menos que se sobrescriban
/// por losa.
///
/// <para>
/// El esqueleto refleja la estructura de la hoja <c>Cargas</c> del libro
/// <c>cargas_estructurales_demo.xlsx</c> (ver <c>docs/referencia/</c>):
/// </para>
/// <list type="bullet">
///   <item>Tabla de carga muerta por espesor (h = 0.06 → 0.20 m, paso 0.01).</item>
///   <item>Pesos propios por uso del nivel (Entrepiso vs Techo).</item>
///   <item>Cargas vivas por uso (Entrepiso, Balcones/Escalera, Techo plano).</item>
///   <item>Factores de combinación según ACI 318-05: <c>qu = 1.2·qd + 1.6·ql</c>.</item>
/// </list>
/// </summary>
public class CargasGlobales : INotifyPropertyChanged
{
    public CargasGlobales()
    {
        CargaMuertaPorEspesor = new TablaCargaMuertaPorEspesor();
        PesosPropiosEntrepiso = new PesosPropiosUso("Entrepiso");
        PesosPropiosTecho     = new PesosPropiosUso("Techo");
        CargasVivas           = new CargasVivasPorUso();
        Factores              = new FactoresCombinacion();
    }

    /// <summary>Tabla h ↔ qd (15 filas para h=0.06..0.20).</summary>
    public TablaCargaMuertaPorEspesor CargaMuertaPorEspesor { get; }

    /// <summary>Mosaicos / Mortero / Pañete (entrepiso, ton/m²).</summary>
    public PesosPropiosUso PesosPropiosEntrepiso { get; }

    /// <summary>Fino / Impermeabilizante / Pañete (techo, ton/m²).</summary>
    public PesosPropiosUso PesosPropiosTecho { get; }

    /// <summary>Cargas vivas (ton/m²) según el uso del nivel.</summary>
    public CargasVivasPorUso CargasVivas { get; }

    /// <summary>Factores de combinación de cargas (default ACI 318-05).</summary>
    public FactoresCombinacion Factores { get; }

    /// <summary>
    /// Construye una <see cref="CargasGlobales"/> con los valores semilla extraídos
    /// del libro <c>cargas_estructurales_demo.xlsx</c> (hoja <c>Cargas</c>).
    /// </summary>
    public static CargasGlobales SemillaPorDefecto()
    {
        var c = new CargasGlobales();

        // Pesos propios entrepiso (xls fila 26-28: Mosaicos 0.069, Mortero 0.072, Pañete 0.040).
        c.PesosPropiosEntrepiso.Items.Add(new PesoPropio { Nombre = "Mosaicos", Espesor = 0.030, Densidad = 2.300 });
        c.PesosPropiosEntrepiso.Items.Add(new PesoPropio { Nombre = "Mortero",  Espesor = 0.040, Densidad = 1.800 });
        c.PesosPropiosEntrepiso.Items.Add(new PesoPropio { Nombre = "Pañete",   Espesor = 0.020, Densidad = 2.000 });

        // Pesos propios techo (mismo patrón; los valores típicos los confirmará el editor
        // del usuario o una semilla más afinada cuando se inspeccione la hoja Cargas
        // filas 65-68 en detalle).
        c.PesosPropiosTecho.Items.Add(new PesoPropio { Nombre = "Fino",            Espesor = 0.030, Densidad = 2.300 });
        c.PesosPropiosTecho.Items.Add(new PesoPropio { Nombre = "Impermeabilizante", Espesor = 0.005, Densidad = 1.000 });
        c.PesosPropiosTecho.Items.Add(new PesoPropio { Nombre = "Pañete",          Espesor = 0.020, Densidad = 2.000 });

        // Carga muerta por espesor: 15 filas h = 0.06 .. 0.20.
        c.CargaMuertaPorEspesor.RegenerarFilas(densidad: 2.4);

        return c;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// =====================================================================
// Tabla de carga muerta por espesor
// =====================================================================

/// <summary>
/// Tabla h ↔ qd usada para calcular la carga muerta de una losa según su espesor
/// equivalente. Espejo de la tabla de la hoja <c>Cargas</c> filas 8-23 (entrepiso)
/// y 47-63 (techo) del libro Excel del ingeniero.
///
/// <para>
/// El qd reportado <b>NO</b> incluye la mampostería sobre la losa (Qmap se suma
/// aparte por <c>CalculoEngine</c>). Sí incluye los pesos propios del uso del
/// nivel — ver fórmula en <see cref="FilaCargaMuerta.Qd"/>.
/// </para>
/// </summary>
public class TablaCargaMuertaPorEspesor : INotifyPropertyChanged
{
    public ObservableCollection<FilaCargaMuerta> Filas { get; } = new();

    /// <summary>
    /// Llena la tabla con 15 filas para h = 0.06 → 0.20 (paso 0.01) usando la
    /// densidad de hormigón armado dada (default 2.4 ton/m³).
    /// </summary>
    public void RegenerarFilas(double densidad = 2.4)
    {
        Filas.Clear();
        for (int i = 6; i <= 20; i++)
        {
            var h = i / 100.0;
            Filas.Add(new FilaCargaMuerta { H = h, Densidad = densidad });
        }
    }

    /// <summary>
    /// Devuelve el qd correspondiente al espesor equivalente <paramref name="hEq"/>.
    /// Hace lookup por la fila de h más cercana sin pasarse (TRUE en VLOOKUP del xls).
    /// </summary>
    /// <param name="hEq">Espesor equivalente (m).</param>
    /// <param name="pesosPropiosTotal">
    /// Suma de los pesos propios (ton/m²) que se sumarán al peso del hormigón
    /// para obtener el qd. Si la fila ya tiene <see cref="FilaCargaMuerta.Qd"/>
    /// pre-computado por el usuario, se usa ese valor y este parámetro se ignora.
    /// </param>
    public double LookupQd(double hEq, double pesosPropiosTotal)
    {
        if (Filas.Count == 0) return 0;
        // ordenar y buscar la mayor h ≤ hEq (VLOOKUP TRUE).
        var ordenadas = Filas.OrderBy(f => f.H).ToList();
        var match = ordenadas.LastOrDefault(f => f.H <= hEq + 1e-9) ?? ordenadas[0];
        return match.Qd > 0 ? match.Qd : (match.WPropio + pesosPropiosTotal);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Una fila de la tabla h ↔ qd. <see cref="WPropio"/> es el peso del hormigón
/// (h·d). <see cref="Qd"/> es el peso muerto total (WPropio + pesos propios).
/// Si <see cref="Qd"/> queda en 0, <see cref="TablaCargaMuertaPorEspesor.LookupQd"/>
/// lo calcula al vuelo sumando los pesos propios pasados como parámetro.
/// </summary>
public class FilaCargaMuerta : INotifyPropertyChanged
{
    private double _h;
    private double _densidad = 2.4;
    private double _qd;

    /// <summary>Espesor de la losa (m).</summary>
    public double H { get => _h; set { _h = value; OnPropertyChanged(); OnPropertyChanged(nameof(WPropio)); } }

    /// <summary>Densidad del hormigón armado (ton/m³). Default 2.4.</summary>
    public double Densidad { get => _densidad; set { _densidad = value; OnPropertyChanged(); OnPropertyChanged(nameof(WPropio)); } }

    /// <summary>Peso propio del hormigón = H · Densidad (ton/m²).</summary>
    public double WPropio => Math.Round(_h * _densidad, 4);

    /// <summary>
    /// qd total para esta fila (ton/m²). Si queda en 0 se calcula al vuelo
    /// sumando los pesos propios del uso. Si el usuario lo edita manualmente
    /// se respeta (override).
    /// </summary>
    public double Qd { get => _qd; set { _qd = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// =====================================================================
// Pesos propios por uso
// =====================================================================

/// <summary>
/// Lista de pesos propios para un uso (entrepiso o techo). Cada item aporta
/// e·d (ton/m²); el total se suma al WPropio del hormigón para obtener qd.
/// </summary>
public class PesosPropiosUso : INotifyPropertyChanged
{
    public PesosPropiosUso(string nombreUso) { NombreUso = nombreUso; }

    /// <summary>Nombre del uso (Entrepiso, Techo, Balcón, ...).</summary>
    public string NombreUso { get; }

    public ObservableCollection<PesoPropio> Items { get; } = new();

    /// <summary>Suma de e·d de todos los items (ton/m²).</summary>
    public double Total => Math.Round(Items.Sum(p => p.W), 4);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Un acabado o capa con espesor y densidad. <see cref="W"/> = e · d (ton/m²).
/// Ej.: Mosaicos (e=0.03 m, d=2.3 ton/m³) → W = 0.069 ton/m².
/// </summary>
public class PesoPropio : INotifyPropertyChanged
{
    private string _nombre = "";
    private double _espesor;
    private double _densidad;

    public string Nombre   { get => _nombre;   set { _nombre = value;   OnPropertyChanged(); } }
    public double Espesor  { get => _espesor;  set { _espesor = value;  OnPropertyChanged(); OnPropertyChanged(nameof(W)); } }
    public double Densidad { get => _densidad; set { _densidad = value; OnPropertyChanged(); OnPropertyChanged(nameof(W)); } }

    /// <summary>Peso por unidad de área (ton/m²) = Espesor · Densidad.</summary>
    public double W => Math.Round(_espesor * _densidad, 4);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "{0} (e={1:0.000}, d={2:0.000}, W={3:0.000})",
            _nombre, _espesor, _densidad, W);
}

// =====================================================================
// Cargas vivas y factores
// =====================================================================

/// <summary>
/// Cargas vivas (ton/m²) según el uso del nivel. Defaults conservadores siguiendo
/// R-001 / ACI: Entrepiso 0.20, Balcones/Escalera 0.40, Techo plano 0.10.
/// </summary>
public class CargasVivasPorUso : INotifyPropertyChanged
{
    private double _entrepiso       = 0.20;
    private double _balconesEscalera = 0.40;
    private double _techoPlano      = 0.10;

    public double Entrepiso        { get => _entrepiso;        set { _entrepiso = value;        OnPropertyChanged(); } }
    public double BalconesEscalera { get => _balconesEscalera; set { _balconesEscalera = value; OnPropertyChanged(); } }
    public double TechoPlano       { get => _techoPlano;       set { _techoPlano = value;       OnPropertyChanged(); } }

    /// <summary>Devuelve la carga viva correspondiente a un <see cref="SistemaUso"/>.</summary>
    public double Para(SistemaUso uso) => uso switch
    {
        SistemaUso.Techo    => _techoPlano,
        SistemaUso.Balcon   => _balconesEscalera,
        SistemaUso.Entrepiso or _ => _entrepiso,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Factores de combinación de cargas. Default ACI 318-05: qu = 1.2·qd + 1.6·ql.
/// </summary>
public class FactoresCombinacion : INotifyPropertyChanged
{
    private double _factorD = 1.2;
    private double _factorL = 1.6;
    private string _norma = "ACI 318-05";

    public double FactorD { get => _factorD; set { _factorD = value; OnPropertyChanged(); } }
    public double FactorL { get => _factorL; set { _factorL = value; OnPropertyChanged(); } }
    public string Norma   { get => _norma;   set { _norma = value;   OnPropertyChanged(); } }

    /// <summary>Aplica la combinación: qu = FactorD·qd + FactorL·ql.</summary>
    public double Combinar(double qd, double ql)
        => Math.Round(_factorD * qd + _factorL * ql, 4);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// =====================================================================
// Enum compartido (uso de un Sistema)
// =====================================================================

/// <summary>
/// Uso de un <see cref="Sistema"/> (nivel de un proyecto). Determina qué carga
/// viva aplica y qué pesos propios suman al qd.
/// </summary>
public enum SistemaUso
{
    /// <summary>Entrepiso típico (residencial/oficina). ql = 0.20 ton/m².</summary>
    Entrepiso,
    /// <summary>Techo plano accesible/no accesible. ql = 0.10 ton/m².</summary>
    Techo,
    /// <summary>Balcones, escaleras y áreas afines. ql = 0.40 ton/m².</summary>
    Balcon,
    /// <summary>Otro uso no estándar — el ingeniero define ql manualmente.</summary>
    Otro,
}

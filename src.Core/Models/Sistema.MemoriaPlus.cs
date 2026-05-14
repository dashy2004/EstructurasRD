using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace LosasPlus.Models;

// ===========================================================================
// Extensiones del modelo de dominio para Memoria Plus.
//
// Este archivo es un partial — las clases base están en Sistema.cs y NO se
// modifican. Aquí solo se agregan campos, propiedades calculadas y constantes
// que usa exclusivamente el flujo de generación de memorias de cálculo:
//   • Proyecto: placeholders (CODIA, teléfonos, suelo, materiales, normas) y
//               configuración global de cargas.
//   • Sistema:  uso (entrepiso/techo/balcón), cota del nivel, salida F. Perdomo.
//   • Losa:     factor K, mampostería, vigueta+bloque (bw, H_blq), override de
//               h_usar, y los outputs computados (h_calc, h_eq, qd, ql, qu).
//
// Defaults pensados para preservar el comportamiento de LosasPlus.App: una
// Losa creada sin tocar estos campos se comporta exactamente como antes.
// ===========================================================================

public partial class Proyecto
{
    private string _codia = "";
    private string _telefonoFijo = "";
    private string _telefonoCelular = "";
    private string _disenadorArquitectonico = "";
    private string _ciudad = "";
    private string _mesAno = "";
    private string _ubicacionCompleta = "";
    private string _uso = "";
    private int    _cantidadNiveles = 1;
    private string _sistemaEstructural = "";
    private string _tipoFundaciones = "";
    private double _esfuerzoAdmisible;       // kg/cm²
    private double _profundidadDesplante;    // m
    private string _otrosParametros = "";
    private double _fcKgCm2 = 280;           // kg/cm² (default residencial)
    private double _fyKgCm2 = 4200;          // kg/cm²
    // Nota: NO se seedea con SemillaPorDefecto en el constructor — eso duplicaría
    // las 15 filas y los items al deserializar JSON (System.Text.Json en modo
    // Populate sumaría a la colección pre-seedeada). La UI llama
    // ProyectoFactory.NuevoProyectoSeedeado() para construir un Proyecto con
    // cargas pre-pobladas. Los archivos cargados con ProyectoSerializer ya traen
    // sus cargas del JSON.
    private CargasGlobales _cargas = new CargasGlobales();

    /// <summary>Código del Colegio Dominicano de Ingenieros, Arquitectos y Agrimensores.</summary>
    public string Codia
    {
        get => _codia;
        set { _codia = value; OnPropertyChanged(); }
    }

    public string TelefonoFijo
    {
        get => _telefonoFijo;
        set { _telefonoFijo = value; OnPropertyChanged(); }
    }

    public string TelefonoCelular
    {
        get => _telefonoCelular;
        set { _telefonoCelular = value; OnPropertyChanged(); }
    }

    /// <summary>Diseñador arquitectónico responsable del proyecto.</summary>
    public string DisenadorArquitectonico
    {
        get => _disenadorArquitectonico;
        set { _disenadorArquitectonico = value; OnPropertyChanged(); }
    }

    public string Ciudad
    {
        get => _ciudad;
        set { _ciudad = value; OnPropertyChanged(); }
    }

    /// <summary>Fecha de portada en formato MM/AAAA (ej. "10/2023").</summary>
    public string MesAno
    {
        get => _mesAno;
        set { _mesAno = value; OnPropertyChanged(); }
    }

    /// <summary>Ubicación completa para la sección de descripción de la memoria.</summary>
    public string UbicacionCompleta
    {
        get => _ubicacionCompleta;
        set { _ubicacionCompleta = value; OnPropertyChanged(); }
    }

    /// <summary>Uso del proyecto (Residencial / Comercial / Industrial / ...).</summary>
    public string Uso
    {
        get => _uso;
        set { _uso = value; OnPropertyChanged(); }
    }

    public int CantidadNiveles
    {
        get => _cantidadNiveles;
        set { _cantidadNiveles = value; OnPropertyChanged(); }
    }

    /// <summary>Sistema estructural (Aporticado / Mixto / Muros de carga / ...).</summary>
    public string SistemaEstructural
    {
        get => _sistemaEstructural;
        set { _sistemaEstructural = value; OnPropertyChanged(); }
    }

    public string TipoFundaciones
    {
        get => _tipoFundaciones;
        set { _tipoFundaciones = value; OnPropertyChanged(); }
    }

    /// <summary>Esfuerzo admisible del suelo de fundación (kg/cm²).</summary>
    public double EsfuerzoAdmisible
    {
        get => _esfuerzoAdmisible;
        set { _esfuerzoAdmisible = value; OnPropertyChanged(); }
    }

    /// <summary>Profundidad de desplante del cimiento (m).</summary>
    public double ProfundidadDesplante
    {
        get => _profundidadDesplante;
        set { _profundidadDesplante = value; OnPropertyChanged(); }
    }

    /// <summary>Texto libre con parámetros adicionales del estudio de suelos.</summary>
    public string OtrosParametros
    {
        get => _otrosParametros;
        set { _otrosParametros = value; OnPropertyChanged(); }
    }

    /// <summary>Resistencia del hormigón f'c en kg/cm² (default 280 ≈ 28 MPa).</summary>
    public double FcKgCm2
    {
        get => _fcKgCm2;
        set { _fcKgCm2 = value; OnPropertyChanged(); }
    }

    /// <summary>Esfuerzo de fluencia del acero fy en kg/cm² (default 4200 ≈ 420 MPa).</summary>
    public double FyKgCm2
    {
        get => _fyKgCm2;
        set { _fyKgCm2 = value; OnPropertyChanged(); }
    }

    /// <summary>Normas y reglamentos aplicables (R-001, R-024, ACI 318-05, ...).</summary>
    public List<string> Normas { get; } = new();

    /// <summary>Configuración global de cargas (tabla por espesor, pesos propios, vivas, factores).</summary>
    public CargasGlobales Cargas
    {
        get => _cargas;
        set { _cargas = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// Factory para construir un <see cref="Proyecto"/> con cargas semilla y otros
/// defaults sensatos. Separado del constructor para no interferir con la
/// deserialización JSON (que necesita un constructor "limpio" para no duplicar
/// colecciones pre-seedadas).
/// </summary>
public static class ProyectoFactory
{
    /// <summary>
    /// Crea un <see cref="Proyecto"/> nuevo con:
    /// - <see cref="CargasGlobales.SemillaPorDefecto"/> ya cargada
    ///   (15 filas tabla h, 3 pesos propios entrepiso, 3 pesos propios techo,
    ///    cargas vivas R-001 y factores ACI 318-05).
    /// - Materiales default (f'c 280 kg/cm², fy 4200 kg/cm²).
    /// </summary>
    /// <remarks>
    /// Para UI: usar este factory al hacer "Nuevo proyecto". Para cargar desde
    /// disco: <c>ProyectoSerializer.Load</c> ya entrega el proyecto con sus
    /// cargas del JSON, sin necesidad de seedear.
    /// </remarks>
    public static Proyecto NuevoProyectoSeedeado()
    {
        var p = new Proyecto();
        p.Cargas = CargasGlobales.SemillaPorDefecto();
        return p;
    }
}

public partial class Sistema
{
    private SistemaUso _uso = SistemaUso.Entrepiso;
    private double _cotaMetros;
    private SalidaPerdomo? _salidaPerdomo;

    /// <summary>
    /// Uso del nivel — afecta qué carga viva aplica y qué pesos propios suman al qd.
    /// Default <see cref="SistemaUso.Entrepiso"/>.
    /// </summary>
    public SistemaUso Uso
    {
        get => _uso;
        set { _uso = value; OnPropertyChanged(); }
    }

    /// <summary>Cota del nivel desde el nivel +0.00 (m). Ej. +2.80 m, +5.60 m.</summary>
    public double CotaMetros
    {
        get => _cotaMetros;
        set { _cotaMetros = value; OnPropertyChanged(); }
    }

    /// <summary>Salida F. Perdomo parseada para este nivel (null si aún no se importó).</summary>
    public SalidaPerdomo? SalidaPerdomo
    {
        get => _salidaPerdomo;
        set { _salidaPerdomo = value; OnPropertyChanged(); OnPropertyChanged(nameof(TieneSalidaPerdomo)); }
    }

    /// <summary>True si el nivel tiene un .txt parseado asociado.</summary>
    [JsonIgnore]
    public bool TieneSalidaPerdomo => _salidaPerdomo != null;
}

public partial class Losa
{
    /// <summary>Factor K para losas 1D (relación Ln/h según ACI 318 9.5.2.1).</summary>
    public static class FactorK
    {
        public const int SimplementeApoyada       = 20;
        public const int UnExtremoContinuo        = 24;
        public const int AmbosExtremosContinuos   = 28;
        public const int Voladizo                 = 10;

        /// <summary>Conjunto de valores válidos para validación de UI.</summary>
        public static readonly int[] ValoresValidos = { 20, 24, 28, 10 };
    }

    private int _k = FactorK.AmbosExtremosContinuos;
    private double _hPisoTecho = 2.8;
    private double _mampN, _mampO, _mampP;       // m lineales por espesor de bloque
    private double? _bw;                          // ancho del nervio (m)
    private double? _hBloque;                     // altura del bloque sobre el nervio (m)
    private double? _hUsarOverride;               // override del h_usar calculado
    private bool   _carryOverride;                // si true, Carga refleja qu calculado

    // ---- Outputs computados (poblados por CalculoEngine) ----
    private double? _hCalc;
    private double? _hEq;
    private double? _qmamp;
    private double? _qmap;
    private double? _qd;
    private double? _ql;
    private double? _qu;

    /// <summary>
    /// Factor K para el cálculo 1D h = Ln/K. Valores válidos {20, 24, 28, 10}.
    /// Default 28 (ambos extremos continuos). Para losas 2D el valor es ignorado.
    /// </summary>
    public int K
    {
        get => _k;
        set { _k = value; OnPropertyChanged(); }
    }

    /// <summary>Altura libre piso-techo sobre la losa (m). Default 2.8 m.</summary>
    public double HPisoTecho
    {
        get => _hPisoTecho;
        set { _hPisoTecho = value; OnPropertyChanged(); }
    }

    /// <summary>Metros lineales de mampostería de espesor 0.20 m sobre la losa.</summary>
    public double MampN { get => _mampN; set { _mampN = value; OnPropertyChanged(); } }

    /// <summary>Metros lineales de mampostería de espesor 0.15 m sobre la losa.</summary>
    public double MampO { get => _mampO; set { _mampO = value; OnPropertyChanged(); } }

    /// <summary>Metros lineales de mampostería de espesor 0.10 m sobre la losa.</summary>
    public double MampP { get => _mampP; set { _mampP = value; OnPropertyChanged(); } }

    /// <summary>Ancho del nervio (m) para losas vigueta+bloque. Null si losa maciza.</summary>
    public double? Bw
    {
        get => _bw;
        set { _bw = value; OnPropertyChanged(); }
    }

    /// <summary>Altura del bloque sobre el nervio (m). Null si losa maciza.</summary>
    public double? HBloque
    {
        get => _hBloque;
        set { _hBloque = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Override manual del espesor a usar (m). Si es null, <c>CalculoEngine</c>
    /// usa el h_calc redondeado (con piso 0.12). Si se setea, se respeta.
    /// </summary>
    public double? HUsarOverride
    {
        get => _hUsarOverride;
        set { _hUsarOverride = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Si <c>true</c>, los cálculos de Memoria Plus escriben <see cref="Qu"/>
    /// también en <see cref="Carga"/> (el campo qu usado por <c>Losas.exe</c>).
    /// Default <c>false</c> para no pisar overrides manuales del LosasPlus app.
    /// </summary>
    public bool CarryQuToCarga
    {
        get => _carryOverride;
        set { _carryOverride = value; OnPropertyChanged(); }
    }

    // ----------------------------------------------------------------
    // Outputs computados (escritos por CalculoEngine.Recalcular)
    // ----------------------------------------------------------------

    /// <summary>
    /// Condición 1D / 2D según relación max/min de luces (>2 → 1D). Calculada
    /// directamente sin engine porque solo depende de Lx/Ly.
    /// </summary>
    [JsonIgnore]
    public string Cond => Math.Max(Lx, Ly) / Math.Max(Math.Min(Lx, Ly), 1e-9) > 2 ? "1D" : "2D";

    /// <summary>Luz de cálculo: Ln = MIN(Lx,Ly) si 1D, MAX(Lx,Ly) si 2D.</summary>
    [JsonIgnore]
    public double Ln => Cond == "1D" ? Math.Min(Lx, Ly) : Math.Max(Lx, Ly);

    /// <summary>Razón max/min de luces (relación de aspecto cruda).</summary>
    [JsonIgnore]
    public double Ratio => Math.Min(Lx, Ly) > 0 ? Math.Max(Lx, Ly) / Math.Min(Lx, Ly) : 0;

    /// <summary>
    /// Dirección de trabajo extendida:
    /// <list type="bullet">
    ///   <item><c>"2D"</c> — bidireccional (ratio ≤ 2, Pieper-Martens).</item>
    ///   <item><c>"1D-V"</c> — una dirección, losa <b>ancha</b> (Lx &gt; Ly):
    ///         flexa en Y, refuerzo principal vertical (strips verticales).</item>
    ///   <item><c>"1D-H"</c> — una dirección, losa <b>alta</b> (Ly &gt; Lx):
    ///         flexa en X, refuerzo principal horizontal (strips horizontales).</item>
    /// </list>
    /// </summary>
    [JsonIgnore]
    public string DireccionTrabajo
    {
        get
        {
            if (Cond == "2D") return "2D";
            return Lx > Ly ? "1D-V" : "1D-H";
        }
    }

    /// <summary>
    /// Ángulo de rotación (grados) para renderizar el icono de "1 dirección"
    /// en la orientación correcta. 0° = strips verticales (losa ancha),
    /// 90° = strips horizontales (losa alta). Para losas 2D el valor no
    /// aplica (la UI usa otro icono).
    /// </summary>
    [JsonIgnore]
    public double DireccionAnguloGrados => Lx > Ly ? 0.0 : 90.0;

    /// <summary>Texto legible para tooltips/UI describiendo la dirección.</summary>
    [JsonIgnore]
    public string DireccionTrabajoTexto => DireccionTrabajo switch
    {
        "2D"   => "Dos direcciones (bidireccional Pieper-Martens)",
        "1D-V" => "Una dirección — losa ancha, refuerzo principal vertical",
        "1D-H" => "Una dirección — losa alta, refuerzo principal horizontal",
        _      => "(indeterminado)",
    };

    /// <summary>Espesor calculado (m) — null hasta que CalculoEngine corra.</summary>
    public double? HCalc { get => _hCalc; set { _hCalc = value; OnPropertyChanged(); OnPropertyChanged(nameof(EspesorInsuficiente)); } }

    /// <summary>Espesor equivalente para losa con vigueta+bloque (m).</summary>
    public double? HEq { get => _hEq; set { _hEq = value; OnPropertyChanged(); } }

    /// <summary>Carga de mampostería sobre la losa (ton).</summary>
    public double? Qmamp { get => _qmamp; set { _qmamp = value; OnPropertyChanged(); } }

    /// <summary>Mampostería distribuida (ton/m²) — Qmamp / Area, mínimo 0.10.</summary>
    public double? Qmap { get => _qmap; set { _qmap = value; OnPropertyChanged(); } }

    /// <summary>Carga muerta total (ton/m²): peso del hormigón + pesos propios + Qmap.</summary>
    public double? Qd { get => _qd; set { _qd = value; OnPropertyChanged(); } }

    /// <summary>Carga viva (ton/m²) según el uso del nivel.</summary>
    public double? Ql { get => _ql; set { _ql = value; OnPropertyChanged(); } }

    /// <summary>Carga última (ton/m²) = FactorD·qd + FactorL·ql.</summary>
    public double? Qu { get => _qu; set { _qu = value; OnPropertyChanged(); } }

    /// <summary>Área de la losa (m²) — Lx · Ly.</summary>
    [JsonIgnore]
    public double Area => Math.Round(Lx * Ly, 4);

    /// <summary>True si <see cref="Espesor"/> &lt; <see cref="HCalc"/> (espesor insuficiente).</summary>
    [JsonIgnore]
    public bool EspesorInsuficiente => _hCalc.HasValue && Espesor + 1e-9 < _hCalc.Value;

    /// <summary>True si Ln &gt; 8.0 m (revisar manualmente — fuera de rango usual).</summary>
    [JsonIgnore]
    public bool LnExcedeRango => Ln > 8.0;
}

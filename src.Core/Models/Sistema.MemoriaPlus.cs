using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using LosasPlus.Cargas;

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

    // -----------------------------------------------------------------
    // ESPESOR EQUIVALENTE — inputs globales (ref: ESPESOR EQUIVALENTE.xlsx)
    // -----------------------------------------------------------------

    private VigaTipo  _vigaPrincipal = new();
    private Bovedilla _bovedilla1D  = new();
    private Bovedilla _bovedilla2D  = new();
    private double    _toppingPorDefecto = 0.05;  // m

    /// <summary>Viga tipo del sistema (b=30 cm, h=70 cm por defecto). Usada para Iviga en αfm.</summary>
    public VigaTipo VigaPrincipal
    {
        get => _vigaPrincipal;
        set { _vigaPrincipal = value ?? new VigaTipo(); OnPropertyChanged(); }
    }

    /// <summary>Geometría de bovedilla para losas <b>1D</b> (default 0.15/0.50/0.50/0.15 m).</summary>
    public Bovedilla Bovedilla1D
    {
        get => _bovedilla1D;
        set { _bovedilla1D = value ?? new Bovedilla(); OnPropertyChanged(); }
    }

    /// <summary>Geometría de bovedilla para losas <b>2D</b> (default 0.15/0.50/0.50/0.15 m).</summary>
    public Bovedilla Bovedilla2D
    {
        get => _bovedilla2D;
        set { _bovedilla2D = value ?? new Bovedilla(); OnPropertyChanged(); }
    }

    /// <summary>Espesor del topping / capeta por defecto (m). Default 0.05 m. Cada losa puede overridear con <see cref="Losa.Topping"/>.</summary>
    public double ToppingPorDefecto
    {
        get => _toppingPorDefecto;
        set { _toppingPorDefecto = value; OnPropertyChanged(); }
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
    /// - <see cref="CombinacionesProyecto.SemillaPorDefecto"/> ASCE 7-05
    ///   (4 casos de carga + 8 combinaciones).
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
        p.Combinaciones = CombinacionesProyecto.SemillaPorDefecto(NormaCombinaciones.Asce7_05);
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

    /// <summary>
    /// Cota del nivel desde el nivel +0.00 (m). Ej. +2.80 m, +5.60 m.
    /// <para>
    /// <b>Obsoleto (B3):</b> la cota es una propiedad de la <see cref="Nivel">planta</see>,
    /// no del sistema. El hogar canónico es <see cref="Nivel.CotaMetros"/> /
    /// <see cref="Nivel.Cota"/>. Se conserva como campo almacenado para cargar
    /// proyectos previos a v4 (la migración v3→v4 lo copia al nivel) y por
    /// compatibilidad con lectores existentes; no usar en código nuevo.
    /// </para>
    /// </summary>
    [Obsolete("Usar Nivel.CotaMetros / Nivel.Cota. Se conserva para back-compat y la migracion v3->v4.")]
    public double CotaMetros
    {
        get => _cotaMetros;
        set { _cotaMetros = value; OnPropertyChanged(); OnPropertyChanged(nameof(Elevacion)); }
    }

    /// <summary>
    /// Elevación del sistema (m) — «cada sistema es un nivel de elevación» (WS3).
    /// Es un <b>alias</b> de <see cref="CotaMetros"/> (comparten almacenamiento).
    /// <para>
    /// <b>Obsoleto (B3):</b> ver <see cref="CotaMetros"/>. El hogar canónico de la
    /// cota/elevación de planta es <see cref="Nivel"/>; este alias sólo persiste
    /// para back-compat y la migración v3→v4.
    /// </para>
    /// </summary>
    [Obsolete("Usar Nivel.CotaMetros / Nivel.Cota. Se conserva para back-compat y la migracion v3->v4.")]
    public double Elevacion
    {
        get => _cotaMetros;
        set { _cotaMetros = value; OnPropertyChanged(); OnPropertyChanged(nameof(CotaMetros)); }
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

    // =====================================================================
    // ESPESOR EQUIVALENTE — outputs adicionales del libro ESPESOR EQUIVALENTE.xlsx
    // (αfm + cantidades de bovedilla + volúmenes + acero distribuido).
    // Todos son nullable: null hasta que CalculoEngine corra el paso correspondiente.
    // =====================================================================

    private double? _alphaX;
    private double? _alphaY;
    private double? _alphaM;
    private string? _estadoAlphaFm;
    private int?    _cantBovedillasX;
    private int?    _cantBovedillasY;
    private int?    _cantBovedillasTotal;
    private double? _vBovedilla;
    private double? _vTotal;
    private double? _vConcreto;
    private double? _topping;

    /// <summary>α (relación de rigideces) en dirección X. <c>Iviga_x / Ilosa_x</c> ACI 9.5.3.3.</summary>
    public double? AlphaX { get => _alphaX; set { _alphaX = value; OnPropertyChanged(); } }

    /// <summary>α en dirección Y. <c>Iviga_y / Ilosa_y</c> ACI 9.5.3.3.</summary>
    public double? AlphaY { get => _alphaY; set { _alphaY = value; OnPropertyChanged(); } }

    /// <summary>α-mean: promedio simple <c>(αx + αy)/2</c>. Criterio ACI: <c>αm &gt; 2</c> → losa con vigas rígidas.</summary>
    public double? AlphaM { get => _alphaM; set { _alphaM = value; OnPropertyChanged(); OnPropertyChanged(nameof(AlphaFmCumple)); } }

    /// <summary>Estado del check ACI 9.5.3.3: <c>"OK"</c> si αm &gt; 2, sino <c>"CHK"</c> (revisar espesor).</summary>
    public string? EstadoAlphaFm { get => _estadoAlphaFm; set { _estadoAlphaFm = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de bovedillas en dirección X (paralelas a Lx).</summary>
    public int? CantBovedillasX { get => _cantBovedillasX; set { _cantBovedillasX = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de bovedillas en dirección Y (paralelas a Ly).</summary>
    public int? CantBovedillasY { get => _cantBovedillasY; set { _cantBovedillasY = value; OnPropertyChanged(); } }

    /// <summary>Total de bovedillas en la losa = M·N.</summary>
    public int? CantBovedillasTotal { get => _cantBovedillasTotal; set { _cantBovedillasTotal = value; OnPropertyChanged(); } }

    /// <summary>Volumen de bovedillas (m³).</summary>
    public double? VBovedilla { get => _vBovedilla; set { _vBovedilla = value; OnPropertyChanged(); } }

    /// <summary>Volumen total de la losa (m³) = h_usar · Lx · Ly.</summary>
    public double? VTotal { get => _vTotal; set { _vTotal = value; OnPropertyChanged(); } }

    /// <summary>Volumen de concreto (m³) = VTotal − VBovedilla. Equivale a la capa maciza por panel.</summary>
    public double? VConcreto { get => _vConcreto; set { _vConcreto = value; OnPropertyChanged(); } }

    /// <summary>Espesor del topping / capeta superior maciza (m). Override del default de proyecto.</summary>
    public double? Topping { get => _topping; set { _topping = value; OnPropertyChanged(); } }

    /// <summary>True si la verificación ACI 9.5.3.3 pasa (αm &gt; 2).</summary>
    [JsonIgnore]
    public bool AlphaFmCumple => _alphaM.HasValue && _alphaM.Value > 2.0;

    /// <summary>
    /// Refuerzo distribuido (barras por diámetro) en X — bottom de vano. Default vacío.
    /// Calculado por <see cref="LosasPlus.Calculo.CalculoEngine.ComputeAsTotal"/>.
    /// </summary>
    public RefuerzoBarras RefuerzoX { get; set; } = new();

    /// <summary>Refuerzo distribuido en Y.</summary>
    public RefuerzoBarras RefuerzoY { get; set; } = new();

    private double? _asxCalc;
    private double? _asyCalc;

    /// <summary>Área total de acero en X (cm²) — sumatoria de <see cref="RefuerzoX"/> · áreas nominales.</summary>
    public double? AsxCalc { get => _asxCalc; set { _asxCalc = value; OnPropertyChanged(); } }

    /// <summary>Área total de acero en Y (cm²) — sumatoria de <see cref="RefuerzoY"/> · áreas nominales.</summary>
    public double? AsyCalc { get => _asyCalc; set { _asyCalc = value; OnPropertyChanged(); } }

    // =====================================================================
    // POSICIÓN EN EL LIENZO CAD (Fase 2 del PLAN_CAD_V1)
    // =====================================================================

    private double? _posX;
    private double? _posY;

    /// <summary>
    /// Coordenada X de la esquina superior-izquierda de la losa en el lienzo
    /// CAD (m). <c>null</c> = losa "flotante": su posición la infiere
    /// <see cref="LosasPlus.Services.LayoutSolver"/> desde las adyacencias.
    /// Cuando tiene valor (junto con <see cref="PosY"/>), la losa está
    /// "anclada" — el solver respeta esa coordenada exacta.
    /// </summary>
    public double? PosX
    {
        get => _posX;
        set { _posX = value; OnPropertyChanged(); OnPropertyChanged(nameof(TienePosicionExplicita)); }
    }

    /// <summary>
    /// Coordenada Y de la esquina superior-izquierda en el lienzo CAD (m),
    /// con eje Y descendente (igual convención que
    /// <c>LayoutSolver.Placement</c>). <c>null</c> = losa flotante.
    /// </summary>
    public double? PosY
    {
        get => _posY;
        set { _posY = value; OnPropertyChanged(); OnPropertyChanged(nameof(TienePosicionExplicita)); }
    }

    /// <summary>
    /// True si la losa tiene posición explícita (ambas <see cref="PosX"/> y
    /// <see cref="PosY"/> con valor) — está "anclada" en el lienzo CAD.
    /// </summary>
    [JsonIgnore]
    public bool TienePosicionExplicita => _posX.HasValue && _posY.HasValue;
}

/// <summary>
/// Cantidades de barras por diámetro estructural para una franja de losa.
/// Los diámetros listados corresponden al inventario habitual de obra en
/// República Dominicana (#3, #4, #5, #6, #7, #8). El método
/// <see cref="LosasPlus.Calculo.CalculoEngine.ComputeAsTotal(RefuerzoBarras)"/>
/// computa el área total <c>As = Σ n·área_nominal</c>.
/// </summary>
public class RefuerzoBarras : INotifyPropertyChanged
{
    private int _n3;
    private int _n4;
    private int _n5;
    private int _n6;
    private int _n7;
    private int _n8;

    /// <summary>Cantidad de barras Ø 3/8" (#3).</summary>
    public int N3 { get => _n3; set { _n3 = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de barras Ø 1/2" (#4).</summary>
    public int N4 { get => _n4; set { _n4 = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de barras Ø 5/8" (#5).</summary>
    public int N5 { get => _n5; set { _n5 = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de barras Ø 3/4" (#6).</summary>
    public int N6 { get => _n6; set { _n6 = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de barras Ø 7/8" (#7).</summary>
    public int N7 { get => _n7; set { _n7 = value; OnPropertyChanged(); } }

    /// <summary>Cantidad de barras Ø 1" (#8).</summary>
    public int N8 { get => _n8; set { _n8 = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Viga tipo del sistema (rectangular). Usada para el cálculo de la rigidez
/// flexionante <c>I_viga = b·h³/12</c> en la verificación αfm (ACI 9.5.3.3).
/// El Excel de referencia (ESPESOR EQUIVALENTE.xlsx) usa b=30 cm, h=70 cm como
/// viga tipo del proyecto; en LosasPlus es editable por proyecto.
/// </summary>
public class VigaTipo : INotifyPropertyChanged
{
    private double _baseCm = 30.0;
    private double _alturaCm = 70.0;

    /// <summary>Base de la viga (cm). Default 30 cm.</summary>
    public double BaseCm { get => _baseCm; set { _baseCm = value; OnPropertyChanged(); OnPropertyChanged(nameof(InerciaCm4)); } }

    /// <summary>Altura total de la viga (cm). Default 70 cm.</summary>
    public double AlturaCm { get => _alturaCm; set { _alturaCm = value; OnPropertyChanged(); OnPropertyChanged(nameof(InerciaCm4)); } }

    /// <summary>Inercia rectangular <c>b·h³/12</c> en cm⁴. Computed.</summary>
    [JsonIgnore]
    public double InerciaCm4 => _baseCm * Math.Pow(_alturaCm, 3) / 12.0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Geometría de bovedilla (bloque/casetón) usada en losas vigueta+bloque.
/// El catálogo dominicano típico es S=0.15, B=0.50, L=0.50, h=0.15 (en metros)
/// tanto para 1D como 2D — pero en LosasPlus se permite editar ambos.
/// </summary>
public class Bovedilla : INotifyPropertyChanged
{
    private double _s = 0.15;   // separación / ancho nervio (m)
    private double _b = 0.50;   // ancho bovedilla (m)
    private double _l = 0.50;   // largo bovedilla (m)
    private double _h = 0.15;   // altura bovedilla (m)

    /// <summary>Separación entre nervios = ancho del nervio (m). Default 0.15.</summary>
    public double S { get => _s; set { _s = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumenIndividual)); } }

    /// <summary>Ancho de la bovedilla (m). Default 0.50.</summary>
    public double B { get => _b; set { _b = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumenIndividual)); } }

    /// <summary>Largo de la bovedilla (m). Default 0.50.</summary>
    public double L { get => _l; set { _l = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumenIndividual)); } }

    /// <summary>Altura de la bovedilla (m). Default 0.15.</summary>
    public double H { get => _h; set { _h = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumenIndividual)); } }

    /// <summary>Volumen de una bovedilla individual = B·L·H (m³). Computed.</summary>
    [JsonIgnore]
    public double VolumenIndividual => _b * _l * _h;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Catálogo de diámetros de barras de refuerzo (norma ASTM A615, número de barra
/// igual a octavos de pulgada). Áreas nominales en cm².
/// </summary>
public static class AreasBarras
{
    /// <summary>Área nominal de una barra Ø 3/8" (#3) en cm² = 0.71.</summary>
    public const double A3 = 0.71;
    /// <summary>Área nominal de una barra Ø 1/2" (#4) en cm² = 1.27.</summary>
    public const double A4 = 1.27;
    /// <summary>Área nominal de una barra Ø 5/8" (#5) en cm² = 1.99.</summary>
    public const double A5 = 1.99;
    /// <summary>Área nominal de una barra Ø 3/4" (#6) en cm² = 2.85.</summary>
    public const double A6 = 2.85;
    /// <summary>Área nominal de una barra Ø 7/8" (#7) en cm² = 3.88.</summary>
    public const double A7 = 3.88;
    /// <summary>Área nominal de una barra Ø 1" (#8) en cm² = 5.07.</summary>
    public const double A8 = 5.07;

    /// <summary>Lookup por número de barra (3..8). Devuelve 0 para valores fuera de rango.</summary>
    public static double Para(int numero) => numero switch
    {
        3 => A3, 4 => A4, 5 => A5, 6 => A6, 7 => A7, 8 => A8,
        _ => 0.0,
    };
}

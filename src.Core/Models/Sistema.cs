using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using LosasPlus.Models.Cad;
using LosasPlus.Cargas;

namespace LosasPlus.Models;

/// <summary>
/// Un proyecto es la raíz del modelo de dominio. Contiene uno o más
/// <see cref="Edificio"/>, cada uno con sus plantas (<see cref="Nivel"/>), y
/// cada nivel con sus <see cref="Sistema"/>s de losas:
/// <code>
/// Proyecto → Edificio → Nivel → Sistema → Losa
/// </code>
/// El proyecto se persiste como un único archivo <c>.lpx.json</c> con metadata
/// (autor, fecha, descripción) más el árbol de edificios. Cada <see cref="Sistema"/>
/// sigue siendo exportable a un <c>.DL</c> byte-compatible con <c>Losas.exe</c>.
///
/// <para>
/// La propiedad <see cref="Sistemas"/> es una <b>fachada de compatibilidad</b>
/// que expone los sistemas del nivel por defecto — ver su documentación.
/// </para>
/// </summary>
public partial class Proyecto : INotifyPropertyChanged
{
    private string _archivo = "";
    private string _nombre = "Proyecto sin título";
    private string _autor = "";
    private string _codigoObra = "";
    private string _ubicacion = "";
    private string _descripcion = "";
    private DateTime _fechaCreacion = DateTime.Now;
    // Backing field con un CombinacionesProyecto VACÍO. La siembra se hace en
    // ProyectoFactory / la migración de ProyectoSerializer, nunca aquí — sembrar
    // en el campo duplicaría al deserializar con Populate. Mismo patrón que Cargas.
    private CombinacionesProyecto _combinaciones = new();

    /// <summary>
    /// Path al archivo de manifest <c>proyecto.lpx.json</c> O, en modo legacy,
    /// al <c>.DL</c> mono-archivo. Si está vacío, el proyecto está en memoria sin guardar.
    /// </summary>
    public string Archivo
    {
        get => _archivo;
        set { _archivo = value; OnPropertyChanged(); OnPropertyChanged(nameof(CarpetaProyecto)); }
    }

    public string Nombre       { get => _nombre;        set { _nombre = value;        OnPropertyChanged(); } }
    public string Autor        { get => _autor;         set { _autor = value;         OnPropertyChanged(); } }
    public string CodigoObra   { get => _codigoObra;    set { _codigoObra = value;    OnPropertyChanged(); } }
    public string Ubicacion    { get => _ubicacion;     set { _ubicacion = value;     OnPropertyChanged(); } }
    public string Descripcion  { get => _descripcion;   set { _descripcion = value;   OnPropertyChanged(); } }
    public DateTime FechaCreacion { get => _fechaCreacion; set { _fechaCreacion = value; OnPropertyChanged(); } }

    /// <summary>Carpeta del proyecto cuando está guardado como manifest. Vacía si es legacy mono-archivo.</summary>
    [JsonIgnore]
    public string? CarpetaProyecto
    {
        get
        {
            if (string.IsNullOrEmpty(_archivo)) return null;
            // Si es proyecto.lpx.json o .lpx → la carpeta es el directorio padre
            var ext = System.IO.Path.GetExtension(_archivo).ToLowerInvariant();
            if (ext == ".json" || ext == ".lpx") return System.IO.Path.GetDirectoryName(_archivo);
            return null;
        }
    }

    /// <summary>
    /// Edificios del proyecto — el almacenamiento real de la jerarquía
    /// <c>Edificio → Nivel → Sistema</c> (Fase 1 de la suite estructural).
    /// Es lo que persiste el <c>.lpx.json</c> v2.
    /// </summary>
    /// <remarks>
    /// Inicializador VACÍO a propósito: <c>ProyectoSerializer</c> usa
    /// <c>PreferredObjectCreationHandling = Populate</c>, que rellena la
    /// colección existente vía <c>.Add()</c>. Sembrar un edificio por defecto
    /// aquí lo duplicaría al deserializar. La estructura por defecto se crea
    /// perezosamente en <see cref="AsegurarEstructura"/>.
    /// </remarks>
    public ObservableCollection<Edificio> Edificios { get; } = new();

    /// <summary>
    /// Sistemas de losas del proyecto — <b>fachada de compatibilidad</b> que
    /// expone los sistemas del primer <see cref="Nivel"/> del primer
    /// <see cref="Edificio"/>. Devuelve la <see cref="ObservableCollection{T}"/>
    /// real (no una copia): todo el código y los bindings previos a la
    /// jerarquía <c>Edificio/Nivel</c> siguen funcionando sin cambios.
    /// No se serializa (<see cref="JsonIgnoreAttribute"/>) — el árbol real
    /// vive en <see cref="Edificios"/>.
    /// </summary>
    [JsonIgnore]
    public ObservableCollection<Sistema> Sistemas
    {
        get
        {
            AsegurarEstructura();
            return Edificios[0].Niveles[0].Sistemas;
        }
    }

    /// <summary>
    /// Garantiza que el proyecto tenga al menos un <see cref="Edificio"/> con
    /// al menos un <see cref="Nivel"/>. Idempotente: no hace nada si la
    /// estructura ya existe. La invoca el getter de <see cref="Sistemas"/> y
    /// puede llamarse tras un <c>new Proyecto()</c> para materializar la
    /// estructura por defecto.
    /// </summary>
    public void AsegurarEstructura()
    {
        if (Edificios.Count == 0)
            Edificios.Add(new Edificio());
        if (Edificios[0].Niveles.Count == 0)
            Edificios[0].Niveles.Add(new Nivel());
    }

    /// <summary>
    /// Casos y combinaciones de carga del proyecto — base de diseño transversal
    /// a todos los módulos (losas, vigas, columnas, zapatas). Fase 2 de la
    /// suite estructural. Se siembra vía <see cref="ProyectoFactory"/>.
    /// </summary>
    public CombinacionesProyecto Combinaciones
    {
        get => _combinaciones;
        set { _combinaciones = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Representa un sistema de losas conforme al formato del archivo .DL del programa Losas.exe
/// (Pieper-Martens, F. Perdomo Ver. 5.00).
/// Estructura: NLOSA, FC, FY, ADICIONALES + lista de losas + bordes adicionales según X / Y.
/// </summary>
public partial class Sistema : INotifyPropertyChanged, IModeloObservable
{
    private string _nombre = "Sistema No 1";
    private double _fc = 0.210;        // ton/cm2
    private double _fy = 4.200;        // ton/cm2
    private int _adicionales = 1;       // 0 = sin camellado, 1 = con camellado

    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Resistencia del hormigón fc' [ton/cm²].</summary>
    public double Fc
    {
        get => _fc;
        set { _fc = value; OnPropertyChanged(); }
    }

    /// <summary>Resistencia del acero fy [ton/cm²].</summary>
    public double Fy
    {
        get => _fy;
        set { _fy = value; OnPropertyChanged(); }
    }

    /// <summary>0: rectas; 1: camelladas (mitad de barras dobladas para apoyo).</summary>
    public int Adicionales
    {
        get => _adicionales;
        set { _adicionales = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Losa> Losas { get; } = new();

    /// <summary>Bordes adicionales en dirección X (continuidad entre losas según X).</summary>
    public ObservableCollection<BordeAdic> BordesX { get; } = new();

    /// <summary>Bordes adicionales en dirección Y (continuidad entre losas según Y).</summary>
    public ObservableCollection<BordeAdic> BordesY { get; } = new();

    /// <summary>
    /// Muros estructurales del sistema (Epic v1.4.0, Módulo Muros v0.9.0).
    /// Elementos verticales dibujados sobre el lienzo CAD. No forman parte
    /// del archivo <c>.DL</c> legacy (Losas.exe no los conoce) — persisten
    /// únicamente en el proyecto.
    /// </summary>
    public ObservableCollection<Muro> Muros { get; } = new();

    public Sistema()
    {
        Losas.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null) foreach (Losa l in e.NewItems) l.PropertyChanged += OnItemChanged;
            if (e.OldItems != null) foreach (Losa l in e.OldItems) l.PropertyChanged -= OnItemChanged;
            NotificarModeloCambiado();
        };
        Muros.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null) foreach (Muro m in e.NewItems) m.PropertyChanged += OnItemChanged;
            if (e.OldItems != null) foreach (Muro m in e.OldItems) m.PropertyChanged -= OnItemChanged;
            NotificarModeloCambiado();
        };
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => NotificarModeloCambiado();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        NotificarModeloCambiado();
    }

    public event System.EventHandler? ModeloCambiado;
    public void NotificarModeloCambiado() => ModeloCambiado?.Invoke(this, System.EventArgs.Empty);
}

/// <summary>
/// Una losa rectangular. Implementa <see cref="IDataErrorInfo"/> para validar in-place
/// cada celda del editor:
/// <list type="bullet">
///   <item><c>Lx</c>, <c>Ly</c>, <c>Espesor</c>, <c>Carga</c> deben ser &gt; 0.</item>
///   <item><c>Rec</c> debe ser ≥ 0 y &lt; <c>Espesor</c>.</item>
/// </list>
/// Además expone <see cref="AspectoFueraDeRango"/>: un warning soft (no error) cuando
/// la relación Ly/Lx queda fuera del rango [0.5, 2.0] cubierto por las tablas de
/// Pieper-Martens.
/// </summary>
public partial class Losa : INotifyPropertyChanged, IDataErrorInfo
{
    /// <summary>Rango de Ly/Lx para el cual las tablas Pieper-Martens son aplicables.</summary>
    public const double AspectoMin = 0.5;
    public const double AspectoMax = 2.0;

    private int _id;
    private int _tipo = 10;
    private double _carga = 2.000;     // ton/m²
    private double _espesor = 0.120;   // m
    private double _lx = 4.000;        // m
    private double _ly = 4.000;        // m
    private double _rec = 0.020;       // m
    private double _coordX;            // m — posición en planta (esquina origen)
    private double _coordY;            // m — posición en planta (esquina origen)

    public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public int Tipo
    {
        get => _tipo;
        set
        {
            _tipo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TipoDescripcion));
            OnPropertyChanged(nameof(TipoEsValido));
        }
    }

    public double Carga
    {
        get => _carga;
        set { _carga = value; OnPropertyChanged(); }
    }

    public double Espesor
    {
        get => _espesor;
        set
        {
            _espesor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Rec)); // Rec valida contra Espesor; refrescar cuando éste cambia.
        }
    }

    public double Lx
    {
        get => _lx;
        set
        {
            _lx = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Aspecto));
            OnPropertyChanged(nameof(AspectoFueraDeRango));
            OnPropertyChanged(nameof(AspectoMensaje));
            OnPropertyChanged(nameof(Cond));
            OnPropertyChanged(nameof(Ln));
            OnPropertyChanged(nameof(Ratio));
            OnPropertyChanged(nameof(DireccionTrabajo));
            OnPropertyChanged(nameof(DireccionAnguloGrados));
            OnPropertyChanged(nameof(DireccionTrabajoTexto));
        }
    }

    public double Ly
    {
        get => _ly;
        set
        {
            _ly = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Aspecto));
            OnPropertyChanged(nameof(AspectoFueraDeRango));
            OnPropertyChanged(nameof(AspectoMensaje));
            OnPropertyChanged(nameof(Cond));
            OnPropertyChanged(nameof(Ln));
            OnPropertyChanged(nameof(Ratio));
            OnPropertyChanged(nameof(DireccionTrabajo));
            OnPropertyChanged(nameof(DireccionAnguloGrados));
            OnPropertyChanged(nameof(DireccionTrabajoTexto));
        }
    }

    public double Rec
    {
        get => _rec;
        set { _rec = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Posición X en planta de la esquina origen del paño, en metros (Fase J —
    /// geometría en planta para el descenso topológico de cargas). El paño ocupa
    /// [X, X+Lx] × [Y, Y+Ly]. Default 0; aditivo en serialización.
    /// </summary>
    public double CoordenadaX
    {
        get => _coordX;
        set { _coordX = value; OnPropertyChanged(); }
    }

    /// <summary>Posición Y en planta de la esquina origen del paño, en metros (ver <see cref="CoordenadaX"/>).</summary>
    public double CoordenadaY
    {
        get => _coordY;
        set { _coordY = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public double Aspecto => _lx > 0 ? Math.Round(_ly / _lx, 3) : 0;
    [JsonIgnore]
    public string TipoDescripcion => TipoLosa.Catalogo.TryGetValue(_tipo, out var t) ? t.Descripcion : "(no estándar)";

    /// <summary>
    /// True si <see cref="Tipo"/> es uno de los 23 códigos permitidos por la
    /// regla de negocio (ver <see cref="TipoLosa.CodigosValidos"/>). La UI lo
    /// usa para marcar en rojo las celdas con tipo no permitido.
    /// </summary>
    [JsonIgnore]
    public bool TipoEsValido => TipoLosa.EsCodigoValido(_tipo);

    /// <summary>True cuando Ly/Lx queda fuera del rango Pieper-Martens. Warning, no error.</summary>
    [JsonIgnore]
    public bool AspectoFueraDeRango => _lx > 0 && (Aspecto < AspectoMin || Aspecto > AspectoMax);

    /// <summary>Mensaje del warning de aspecto (vacío si está en rango).</summary>
    [JsonIgnore]
    public string AspectoMensaje => AspectoFueraDeRango
        ? string.Format(CultureInfo.InvariantCulture,
            "Ly/Lx = {0:0.00} está fuera del rango Pieper-Martens [{1:0.0}, {2:0.0}]. " +
            "El motor puede aceptarlo, pero los coeficientes de las tablas pueden ser imprecisos.",
            Aspecto, AspectoMin, AspectoMax)
        : "";

    // ---------- Resultados (poblados por el parser de .TXT) ----------
    public double? Mfx { get; set; }
    public double? Mfy { get; set; }
    public double? MSx { get; set; }
    public double? MSy { get; set; }
    public string? AsxVano { get; set; }
    public string? AsyVano { get; set; }

    // ---------- IDataErrorInfo ----------
    string IDataErrorInfo.this[string columnName] => columnName switch
    {
        nameof(Lx)      => _lx      <= 0 ? "Lx debe ser mayor que 0 (en metros)." : "",
        nameof(Ly)      => _ly      <= 0 ? "Ly debe ser mayor que 0 (en metros)." : "",
        nameof(Espesor) => _espesor <= 0 ? "Espesor (H) debe ser mayor que 0 (en metros)." : "",
        nameof(Carga)   => _carga   <= 0 ? "Carga Wu debe ser mayor que 0 (en ton/m²)." : "",
        nameof(Rec)     => ValidateRec(),
        nameof(Tipo)    => ValidateTipo(),
        _ => ""
    };

    string IDataErrorInfo.Error
    {
        get
        {
            var dei = (IDataErrorInfo)this;
            var errors = new[] { nameof(Lx), nameof(Ly), nameof(Espesor), nameof(Carga), nameof(Rec), nameof(Tipo) }
                .Select(p => dei[p])
                .Where(e => !string.IsNullOrEmpty(e));
            return string.Join(" • ", errors);
        }
    }

    private string ValidateRec()
    {
        if (_rec < 0) return "Recubrimiento no puede ser negativo.";
        if (_espesor > 0 && _rec >= _espesor)
            return $"Recubrimiento ({_rec:0.000} m) debe ser menor que el espesor ({_espesor:0.000} m).";
        return "";
    }

    private string ValidateTipo()
    {
        if (TipoLosa.EsCodigoValido(_tipo)) return "";
        return $"Tipo {_tipo} no permitido. Solo se aceptan los 23 tipos del " +
               "catálogo Pieper-Martens (10, 13, 14, 21–24, 31–34, 40, 43, 44, " +
               "51–54, 60, 63, 64, 71, 72).";
    }

    /// <summary>Conveniencia para tests y plugins: ¿la losa pasa todas las validaciones duras?</summary>
    [JsonIgnore]
    public bool EsValida => string.IsNullOrEmpty(((IDataErrorInfo)this).Error);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Borde de continuidad entre dos losas (campo I, campo J) con bandera de balanceo.
/// Conforme a Losas.hlp: ADIC. SEGUN X / Y → B-I, B-J, BALANCEO (S/N).
/// Las propiedades <c>Mu*</c>, <c>D</c>, <c>AsReq</c>, <c>Disponer</c> se rellenan
/// al importar un <c>.TXT</c> de salida del motor.
/// </summary>
public class BordeAdic : INotifyPropertyChanged
{
    private int _bi = 1;
    private int _bj = 2;
    private string _balanceo = "S"; // S = aplica balanceo, N = no balancea

    public int BI { get => _bi; set { _bi = value; OnPropertyChanged(); } }
    public int BJ { get => _bj; set { _bj = value; OnPropertyChanged(); } }

    /// <summary>"S" para balanceo (caso normal) o "N" (voladizos / tramos cortos / nudos de 3 lados).</summary>
    public string Balanceo
    {
        get => _balanceo;
        set { _balanceo = (value ?? "S").Trim().ToUpperInvariant() switch { "N" => "N", _ => "S" }; OnPropertyChanged(); }
    }

    // Resultados parseados del .TXT (TxtParser.Apply los rellena)
    public double? MuI { get; set; }
    public double? MuJ { get; set; }
    public double? MuIJ { get; set; }
    public double? D { get; set; }
    public double? AsReq { get; set; }
    public string? Disponer { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString()
        => $"{BI} – {BJ} ({Balanceo})";
}

/// <summary>Tipo de borde de una losa según la convención visual de F. Perdomo.</summary>
public enum BorderKind
{
    /// <summary>Línea fina simple — apoyo simple (muro/viga sin continuidad).</summary>
    Apoyado,
    /// <summary>Línea con rayado perpendicular hacia adentro — borde continuo / empotrado.</summary>
    Empotrado,
    /// <summary>Línea punteada — borde libre (sin apoyo).</summary>
    Libre,
    /// <summary>Línea punteada — voladizo (la losa vuela en este borde).</summary>
    Vuelo,
}

/// <summary>
/// Categoría visual de alto nivel derivada del patrón de bordes de un tipo. Sirve para
/// filtrado/agrupación en UI sin perder los 24 códigos numéricos del catálogo.
/// </summary>
public enum CategoriaVisual
{
    /// <summary>Sin empotramientos: 4 bordes apoyados (con o sin variantes).</summary>
    Libre,
    /// <summary>Con al menos un borde empotrado, sin vuelos.</summary>
    Empotrada,
    /// <summary>Mezcla de empotramiento(s) + algún vuelo (variante con voladizo adyacente).</summary>
    ConVuelo,
    /// <summary>Voladizo puro: predominio de vuelos (caso 71/72 y similares).</summary>
    Voladizo,
}

/// <summary>Orientación visual del tipo (cuando aplica). Derivada de cuál borde lleva el rasgo dominante.</summary>
public enum Orientacion
{
    /// <summary>El rasgo principal (empotrado o vuelo) está en N o S — orientación HORIZONTAL.</summary>
    Horizontal,
    /// <summary>El rasgo principal está en E o W — orientación VERTICAL.</summary>
    Vertical,
    /// <summary>Sin rasgo direccional dominante (4 bordes iguales o esquina simétrica).</summary>
    Indistinta,
}

public sealed record TipoLosa
{
    public int Codigo { get; init; }
    public string Descripcion { get; init; } = "";
    /// <summary>Patrón de bordes [Norte, Este, Sur, Oeste].</summary>
    public BorderKind[] Bordes { get; init; } = new BorderKind[4];

    public TipoLosa(int codigo, string descripcion, BorderKind n, BorderKind e, BorderKind s, BorderKind w)
    {
        Codigo = codigo;
        Descripcion = descripcion;
        Bordes = new[] { n, e, s, w };
    }

    /// <summary>Icono textual derivado del patrón (compat con código existente).</summary>
    public string BordesIco => string.Join(" ", Bordes.Select(b => b switch
    {
        BorderKind.Empotrado => "═",
        BorderKind.Libre     => "·",
        BorderKind.Vuelo     => "·",
        _                    => "─",
    }));

    /// <summary>Conteo de bordes empotrados (continuidad) — compat con código existente.</summary>
    public int BordesContinuos => Bordes.Count(b => b == BorderKind.Empotrado);

    /// <summary>Cantidad de bordes con voladizo (Vuelo o Libre tratados juntos como "no apoyo").</summary>
    public int BordesVuelo => Bordes.Count(b => b == BorderKind.Vuelo || b == BorderKind.Libre);

    /// <summary>
    /// Categoría visual derivada del patrón de bordes. Permite filtrar/agrupar tipos
    /// sin perder los 24 códigos numéricos del catálogo Pieper-Martens.
    /// Reglas:
    /// <list type="bullet">
    ///   <item>Si la mayoría de bordes son Vuelo/Libre (≥3 de 4) → <see cref="CategoriaVisual.Voladizo"/>.</item>
    ///   <item>Si hay al menos 1 vuelo Y al menos 1 empotrado → <see cref="CategoriaVisual.ConVuelo"/>.</item>
    ///   <item>Si hay al menos 1 empotrado y sin vuelos → <see cref="CategoriaVisual.Empotrada"/>.</item>
    ///   <item>Resto (4 apoyados, sin vuelos ni empotramientos) → <see cref="CategoriaVisual.Libre"/>.</item>
    /// </list>
    /// </summary>
    public CategoriaVisual Categoria
    {
        get
        {
            int empot = BordesContinuos;
            int vuelo = BordesVuelo;
            if (vuelo >= 3) return CategoriaVisual.Voladizo;
            if (vuelo >= 1 && empot >= 1) return CategoriaVisual.ConVuelo;
            if (empot >= 1) return CategoriaVisual.Empotrada;
            return CategoriaVisual.Libre;
        }
    }

    /// <summary>
    /// Orientación dominante: si el rasgo principal (empotrado o vuelo) cae en N/S es Horizontal,
    /// si cae en E/W es Vertical, si es simétrica o se reparten 50/50 es Indistinta.
    /// </summary>
    public Orientacion Orientacion
    {
        get
        {
            // "Rasgo" = bordes que no son apoyo simple (empotrados o vuelos).
            bool n = Bordes[0] != BorderKind.Apoyado;
            bool e = Bordes[1] != BorderKind.Apoyado;
            bool s = Bordes[2] != BorderKind.Apoyado;
            bool w = Bordes[3] != BorderKind.Apoyado;
            int horiz = (n ? 1 : 0) + (s ? 1 : 0);  // N/S
            int vert  = (e ? 1 : 0) + (w ? 1 : 0);  // E/W
            if (horiz > vert) return Orientacion.Horizontal;
            if (vert > horiz) return Orientacion.Vertical;
            return Orientacion.Indistinta;
        }
    }

    /// <summary>
    /// Catálogo de tipos basado en el catálogo visual de F. Perdomo (Losas v5.20).
    /// Bordes en orden Norte/Este/Sur/Oeste. Convención visual:
    /// Apoyado = línea fina, Empotrado = rayado (continuo), Libre/Vuelo = punteado.
    /// </summary>
    public static readonly Dictionary<int, TipoLosa> Catalogo = new()
    {
        // 1x — sin empotramientos (apoyo simple en los 4 bordes, eventualmente con vuelos)
        [10] = new(10, "Cuatro bordes simplemente apoyados",
            BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado),
        [13] = new(13, "Apoyo simple + un vuelo (variante)",
            BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Vuelo),
        [14] = new(14, "Apoyo simple + dos vuelos (variante)",
            BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Vuelo, BorderKind.Vuelo),

        // 2x — un borde continuo
        [21] = new(21, "Un borde continuo en X (perpendicular a Lx)",
            BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Apoyado),
        [22] = new(22, "Un borde continuo en Y (perpendicular a Ly)",
            BorderKind.Apoyado, BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Apoyado),
        [23] = new(23, "Un borde continuo + un vuelo (variante)",
            BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Apoyado, BorderKind.Vuelo),
        [24] = new(24, "Un borde continuo + dos vuelos (variante)",
            BorderKind.Empotrado, BorderKind.Vuelo,   BorderKind.Apoyado, BorderKind.Vuelo),

        // 3x — dos bordes continuos
        [31] = new(31, "Dos bordes continuos paralelos en X",
            BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Empotrado, BorderKind.Apoyado),
        [32] = new(32, "Dos bordes continuos paralelos en Y",
            BorderKind.Apoyado, BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Empotrado),
        [33] = new(33, "Dos bordes continuos adyacentes (esquina)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Apoyado),
        [34] = new(34, "Dos bordes continuos + un vuelo (variante)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Apoyado),

        // 4x — tres bordes continuos
        [40] = new(40, "Tres bordes continuos (libre el cuarto en X)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Empotrado),
        [43] = new(43, "Tres bordes continuos + un vuelo (variante)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Empotrado),
        [44] = new(44, "Tres bordes continuos + dos vuelos (variante)",
            BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Empotrado, BorderKind.Vuelo),

        // 5x — perimetral con vuelos / variantes
        [51] = new(51, "Tres bordes continuos + variante de orientación",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Apoyado, BorderKind.Empotrado),
        [52] = new(52, "Cuatro bordes continuos + variante de orientación",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Empotrado),
        [53] = new(53, "Cuatro bordes continuos + un vuelo (variante)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Vuelo),
        [54] = new(54, "Cuatro bordes continuos + dos vuelos (variante)",
            BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Empotrado, BorderKind.Vuelo),

        // 6x — perimetral total
        [60] = new(60, "Cuatro bordes empotrados (perimetral)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Empotrado),
        [63] = new(63, "Perimetral + un vuelo (variante)",
            BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Empotrado, BorderKind.Vuelo),
        [64] = new(64, "Perimetral + dos vuelos (variante)",
            BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Empotrado, BorderKind.Vuelo),

        // 7x — voladizos puros
        [71] = new(71, "Voladizo según X (apoyado en N, vuela en E/S/W)",
            BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Vuelo, BorderKind.Vuelo),
        [72] = new(72, "Voladizo según Y (apoyado en E, vuela en N/S/W)",
            BorderKind.Vuelo, BorderKind.Empotrado, BorderKind.Vuelo, BorderKind.Vuelo),
    };

    public static string FormatTabla(int codigo)
        => Catalogo.TryGetValue(codigo, out var t) ? $"{t.Codigo} — {t.Descripcion}" : codigo.ToString(CultureInfo.InvariantCulture);

    // =====================================================================
    // VALIDACIÓN DE CÓDIGOS — los 23 tipos permitidos por la regla de negocio
    // =====================================================================

    /// <summary>
    /// Conjunto de los 23 códigos de tipo de losa <b>permitidos</b>. Es la
    /// fuente única de verdad de la regla de negocio: la aplicación solo
    /// procesa estos tipos. Derivado directamente de <see cref="Catalogo"/>
    /// (que ya contiene exactamente esos 23).
    /// </summary>
    public static readonly IReadOnlySet<int> CodigosValidos =
        new HashSet<int>(Catalogo.Keys);

    /// <summary>
    /// Aliases legacy de códigos retirados: tipos cuyo patrón de bordes es
    /// <b>idéntico</b> a un código canónico, por lo que se pueden remapear
    /// con seguridad al cargar archivos viejos.
    /// <list type="bullet">
    ///   <item><c>11 → 10</c> — ambos: cuatro bordes simplemente apoyados.</item>
    ///   <item><c>50 → 60</c> — ambos: cuatro bordes empotrados (perimetral).</item>
    /// </list>
    /// El código <c>41</c> NO se incluye: su orientación de bordes difiere de
    /// 40 (no es un alias seguro), así que se trata como tipo no permitido.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int> AliasesLegacy =
        new Dictionary<int, int> { [11] = 10, [50] = 60 };

    /// <summary>True si <paramref name="codigo"/> es uno de los 23 tipos permitidos.</summary>
    public static bool EsCodigoValido(int codigo) => CodigosValidos.Contains(codigo);

    /// <summary>
    /// Normaliza un código: si es un alias legacy conocido (ver
    /// <see cref="AliasesLegacy"/>) devuelve el código canónico equivalente;
    /// en cualquier otro caso devuelve el código sin cambios. Usar al
    /// <b>parsear archivos</b> para absorber .DL antiguos sin perder datos.
    /// </summary>
    public static int NormalizarCodigo(int codigo)
        => AliasesLegacy.TryGetValue(codigo, out var canonico) ? canonico : codigo;
}

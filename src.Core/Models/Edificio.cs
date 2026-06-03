using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LosasPlus.Models.Cad;
using LosasPlus.Vigas;

namespace LosasPlus.Models;

/// <summary>
/// Un <see cref="Edificio"/> agrupa las plantas (<see cref="Nivel"/>) de una
/// construcción. Es el primer escalón de la jerarquía de dominio introducida
/// por la expansión a suite estructural (Fase 1):
/// <code>
/// Proyecto → Edificio → Nivel → Sistema → Losa
/// </code>
///
/// <para>
/// Un <see cref="Proyecto"/> puede contener varios edificios; cada edificio
/// apila sus <see cref="Niveles"/> de la cota más baja a la más alta. En las
/// fases siguientes el <see cref="Nivel"/> colgará también columnas, vigas y
/// zapatas — hoy sólo agrupa <see cref="Sistema"/>s de losas.
/// </para>
///
/// <para>
/// Tipo <b>puro de dominio</b>: no referencia <c>System.Windows</c> ni ningún
/// tipo de WPF — vive en <c>src.Core</c> y es reutilizable desde cualquier
/// consumidor .NET.
/// </para>
/// </summary>
public partial class Edificio : INotifyPropertyChanged, IModeloObservable
{
    private string _nombre = "Edificio 1";

    private double _latitud;
    private double _longitud;

    /// <summary>Nombre descriptivo del edificio.</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Latitud geográfica del edificio en grados decimales (Fase K — cimiento de
    /// la georreferenciación / mapa 3D). 0 = sin georreferenciar. Aditivo.
    /// </summary>
    public double Latitud
    {
        get => _latitud;
        set { _latitud = value; OnPropertyChanged(); }
    }

    /// <summary>Longitud geográfica del edificio en grados decimales (ver <see cref="Latitud"/>).</summary>
    public double Longitud
    {
        get => _longitud;
        set { _longitud = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Plantas del edificio, ordenadas convencionalmente de la cota más baja a
    /// la más alta. Colección get-only con inicializador — <c>ProyectoSerializer</c>
    /// la rellena vía <c>Populate</c> al deserializar.
    /// </summary>
    public ObservableCollection<Nivel> Niveles { get; } = new();

    /// <summary>
    /// Ejes / rejillas estructurales del edificio (WS2) — líneas de referencia
    /// compartidas por todas las plantas. Aditiva; no afecta el cálculo, sólo
    /// organiza la geometría y habilita las vistas de sección del 3D.
    /// </summary>
    public ObservableCollection<EjeEstructural> Ejes { get; } = new();

    public Edificio()
    {
        Niveles.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null) foreach (Nivel n in e.NewItems) n.ModeloCambiado += OnNivelCambiado;
            if (e.OldItems != null) foreach (Nivel n in e.OldItems) n.ModeloCambiado -= OnNivelCambiado;
            NotificarModeloCambiado();
        };
    }

    private void OnNivelCambiado(object? sender, System.EventArgs e) => NotificarModeloCambiado();

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
/// Un <see cref="Nivel"/> es una planta de un <see cref="Edificio"/>: agrupa
/// los elementos estructurales que comparten una misma cota.
///
/// <para>
/// En la Fase 1 un nivel sólo contiene <see cref="Sistema"/>s de losas; las
/// fases siguientes le añadirán columnas, vigas y zapatas. La migración del
/// formato <c>.lpx.json</c> v1→v2 envuelve todos los sistemas heredados en un
/// único nivel por defecto.
/// </para>
///
/// <para>Tipo puro de dominio — sin dependencias de WPF.</para>
/// </summary>
public partial class Nivel : INotifyPropertyChanged, IModeloObservable
{
    private string _nombre = "Nivel 1";
    private double _cota;   // m

    /// <summary>Nombre de la planta (p. ej. «Planta Baja», «Nivel 2», «Techo»).</summary>
    public string Nombre
    {
        get => _nombre;
        set { _nombre = value; OnPropertyChanged(); }
    }

    /// <summary>Elevación de la planta respecto al origen del edificio, en metros.</summary>
    public double Cota
    {
        get => _cota;
        set { _cota = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Sistemas de losas de esta planta. Colección get-only con inicializador
    /// — se rellena vía <c>Populate</c> al deserializar.
    /// </summary>
    public ObservableCollection<Sistema> Sistemas { get; } = new();

    /// <summary>
    /// Vigas continuas de esta planta — el frente estructural de vigas de la
    /// Fase 3. Colección get-only con inicializador; se (de)serializa vía
    /// <c>Populate</c> igual que <see cref="Sistemas"/>, de forma aditiva
    /// (los proyectos previos cargan con la colección vacía).
    /// </summary>
    public ObservableCollection<Viga> Vigas { get; } = new();

    /// <summary>
    /// Columnas de esta planta (Fase J — descenso topológico de cargas). Colección
    /// get-only con inicializador; se (de)serializa vía <c>Populate</c> igual que
    /// <see cref="Vigas"/>, de forma aditiva (los proyectos previos cargan con la
    /// colección vacía).
    /// </summary>
    public ObservableCollection<Columna> Columnas { get; } = new();

    public Nivel()
    {
        Sistemas.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null) foreach (Sistema sys in e.NewItems) sys.ModeloCambiado += OnHijoCambiado;
            if (e.OldItems != null) foreach (Sistema sys in e.OldItems) sys.ModeloCambiado -= OnHijoCambiado;
            NotificarModeloCambiado();
        };
        Vigas.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null) foreach (Viga v in e.NewItems) v.PropertyChanged += OnItemChanged;
            if (e.OldItems != null) foreach (Viga v in e.OldItems) v.PropertyChanged -= OnItemChanged;
            NotificarModeloCambiado();
        };
        Columnas.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null) foreach (Columna c in e.NewItems) c.PropertyChanged += OnItemChanged;
            if (e.OldItems != null) foreach (Columna c in e.OldItems) c.PropertyChanged -= OnItemChanged;
            NotificarModeloCambiado();
        };
    }

    private void OnHijoCambiado(object? sender, System.EventArgs e) => NotificarModeloCambiado();
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

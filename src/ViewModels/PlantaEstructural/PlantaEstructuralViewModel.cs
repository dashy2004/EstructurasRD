using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Grillas;
using LosasPlus.Models;
using LosasPlus.Topologia;

namespace LosasPlus.ViewModels.PlantaEstructural;

/// <summary>
/// Modos de interacción de la herramienta activa en la Planta
/// Estructural (Liga C paso C2). Mismo concepto que
/// <c>ModoHerramienta3D</c> del viewport 3D B3, pero aplicado al
/// canvas 2D.
/// </summary>
public enum ModoHerramientaPlanta
{
    /// <summary>Selección + drag para mover columnas/zapatas existentes.</summary>
    Seleccion,
    /// <summary>1 click crea una nueva Columna en el nivel activo (con snap a grilla).</summary>
    CrearColumna,
    /// <summary>2 clicks (inicio + fin) crea una nueva Viga con snap a grilla en ambos extremos.</summary>
    CrearViga,
    /// <summary>1 click sobre un elemento estructural lo elimina (Ghost Deletion via IAutoria3DService).</summary>
    BorrarElemento,
}

/// <summary>
/// ViewModel del modo "Planta Estructural" — vista 2D estática del
/// nivel activo (Liga C paso C1). Expone al canvas:
/// <list type="bullet">
///   <item><see cref="NivelActivo"/>: nivel cuyas columnas/vigas/zapatas
///   se renderizan.</item>
///   <item><see cref="NivelesDisponibles"/>: lista de todos los niveles
///   del proyecto, alimentando el combobox de selección.</item>
///   <item><see cref="GrillaActiva"/>: grilla estructural del edificio
///   (A/B/C × 1/2/3) para dibujar los ejes.</item>
///   <item><see cref="Revision"/>: token entero incrementado cada vez
///   que cambia la selección de nivel o el proyecto muta — el canvas
///   bindea a esto via DependencyProperty para disparar un redraw.</item>
///   <item><see cref="Seleccion"/>: servicio singleton inyectado para
///   que el canvas publique <c>DomainKey</c> al hacer click en un
///   elemento estructural.</item>
/// </list>
///
/// <para>
/// El VM no muta el dominio en C1 — la edición por drag/2-click llegará
/// en C2. Aquí solo observa.
/// </para>
/// </summary>
public sealed class PlantaEstructuralViewModel : INotifyPropertyChanged
{
    private readonly Proyecto _proyecto;
    private Nivel? _nivelActivo;
    private int _revision;

    public PlantaEstructuralViewModel(Proyecto proyecto, SeleccionService seleccion)
    {
        _proyecto = proyecto;
        Seleccion = seleccion;
        // Inicializa al primer nivel del primer edificio si existe.
        _nivelActivo = proyecto.Edificios.FirstOrDefault()?.Niveles.FirstOrDefault();
        // Default: primer Sistema del nivel activo (donde viven las losas
        // del editor CAD legacy). Si no hay, queda null.
        _sistemaActivo = _nivelActivo?.Sistemas.FirstOrDefault();
    }

    private Sistema? _sistemaActivo;
    private ModoHerramientaPlanta _herramienta = ModoHerramientaPlanta.Seleccion;
    private bool _mostrarIndicadoresNodales;

    /// <summary>
    /// Toggle de los indicadores de conexión nodal sobre la planta
    /// (Liga E paso E3). Cuando es <c>true</c>, el canvas pinta círculos
    /// verdes (≥2 incidentes — conectado) o naranjas (=1 — flotante)
    /// en cada nodo del <c>GrafoProyectadoBuilder</c> que cae en la
    /// cota del nivel activo.
    /// </summary>
    public bool MostrarIndicadoresNodales
    {
        get => _mostrarIndicadoresNodales;
        set
        {
            if (_mostrarIndicadoresNodales == value) return;
            _mostrarIndicadoresNodales = value;
            OnPropertyChanged();
            InvalidarVista();
        }
    }

    /// <summary>
    /// Herramienta activa de la toolbar (Liga C paso C2). El canvas
    /// observa esta propiedad para conmutar entre los handlers de
    /// click/drag (Selección/Crear*/Borrar).
    /// </summary>
    public ModoHerramientaPlanta HerramientaActiva
    {
        get => _herramienta;
        set
        {
            if (_herramienta == value) return;
            _herramienta = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Sistema cuyas losas + muros se renderizan en planta (mismo concepto
    /// que <c>MainViewModel.SistemaActivo</c>; lo sincronizamos en C1.5
    /// para que la Planta Estructural unifique la visualización CAD +
    /// estructural sin necesidad de tener dos modos paralelos).
    /// </summary>
    public Sistema? SistemaActivo
    {
        get => _sistemaActivo;
        set
        {
            if (ReferenceEquals(_sistemaActivo, value)) return;
            _sistemaActivo = value;
            OnPropertyChanged();
            InvalidarVista();
        }
    }

    /// <summary>
    /// Singleton de selección compartido con el resto del shell — el
    /// canvas publica aquí los clicks, y el panel "Elemento Activo"
    /// + el routing de modos del MainViewModel reaccionan.
    /// </summary>
    public SeleccionService Seleccion { get; }

    /// <summary>
    /// Nivel cuyas entidades se renderizan. Cambiar este valor dispara
    /// un incremento de <see cref="Revision"/> y, por extensión, un
    /// redraw del canvas.
    /// </summary>
    public Nivel? NivelActivo
    {
        get => _nivelActivo;
        set
        {
            if (ReferenceEquals(_nivelActivo, value)) return;
            _nivelActivo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GrillaActiva));
            InvalidarVista();
        }
    }

    /// <summary>
    /// Lista observable de todos los niveles del proyecto — alimenta
    /// el combobox de la toolbar. Se computa on-demand desde
    /// <see cref="Proyecto.Edificios"/>.
    /// </summary>
    public ObservableCollection<Nivel> NivelesDisponibles
    {
        get
        {
            var lista = new ObservableCollection<Nivel>();
            foreach (var edif in _proyecto.Edificios)
                foreach (var n in edif.Niveles)
                    lista.Add(n);
            return lista;
        }
    }

    /// <summary>
    /// Grilla estructural del edificio activo (primer edificio por
    /// convención M2). <c>null</c> si no hay edificios.
    /// </summary>
    public GrillaEstructural? GrillaActiva
        => _proyecto.Edificios.FirstOrDefault()?.Grillas;

    /// <summary>
    /// Token entero — incrementado cada vez que el canvas debe
    /// re-renderizarse. El canvas lo bindea via DependencyProperty;
    /// su PropertyChanged callback dispara la invalidación visual.
    /// </summary>
    public int Revision
    {
        get => _revision;
        private set { _revision = value; OnPropertyChanged(); }
    }

    /// <summary>Dispara un redraw del canvas incrementando el token.</summary>
    public void InvalidarVista() => Revision++;

    /// <summary>
    /// Proyecto raíz — expuesto para que el canvas resuelva las
    /// colecciones del nivel y el grafo proyectado.
    /// </summary>
    public Proyecto Proyecto => _proyecto;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Helper marker — vacío por ahora (placeholder para extensión futura).
    private interface INotifyCollectionChangedHelper { }
}

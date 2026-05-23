using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;          // Color de WPF (consumido por LineGeometryModel3D.Color)
using System.Windows.Threading;
using HelixToolkit;                  // Vector3Collection, IntCollection
using HelixToolkit.SharpDX;          // LineGeometry3D
using HelixToolkit.Wpf.SharpDX;      // LineGeometryModel3D
using LosasPlus.Models;
using LosasPlus.Topologia;           // GrafoProyectadoBuilder, IGrafoEstructural, DomainKey, TipoElemento

namespace LosasPlus.ViewModels.Viewport3D;

/// <summary>
/// Servicio puro que sintetiza la geometría wireframe 3D del proyecto a
/// partir del grafo estructural derivado en <c>src.Core</c>
/// (<see cref="GrafoProyectadoBuilder"/> — Fase 3D-I2 del Plan Maestro).
///
/// <para>
/// <b>Pipeline</b> (Fase 3D-I2):
/// <list type="number">
/// <item>El motor analítico <see cref="GrafoProyectadoBuilder.Construir"/>
/// (puro, sin dependencias gráficas) corre en <see cref="Task.Run"/> y
/// produce un <see cref="IGrafoEstructural"/> inmutable con nodos
/// consolidados a tolerancia de 1 mm (default ETABS / SAP2000).</item>
/// <item>Cada <see cref="AristaSintetica"/> del grafo se traduce a un
/// par <see cref="LineGeometry3D"/> + color del tema, ambos
/// thread-agnósticos.</item>
/// <item>El swap a la <see cref="ObservableElement3DCollection"/> bindeada
/// al <c>Viewport3DX</c> se realiza en el hilo de UI vía
/// <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// El <c>Tag</c> de cada <see cref="LineGeometryModel3D"/> es un
/// <see cref="DomainKey"/> (Tipo + Id del elemento original), suficiente
/// para que el hit-testing nativo de Fase 3D-I3 resuelva la selección
/// hacia el modelo de dominio sin un diccionario auxiliar.
/// </para>
///
/// <para>
/// La grilla artificial de la Iteración 1 desapareció: ahora los muros
/// se renderizan en sus coordenadas reales (vía <c>PuntoCad</c>),
/// columnas y zapatas se distribuyen de forma <b>determinista</b> por
/// su <c>Id</c> en la grilla virtual del builder (paso 6 m), y
/// Columna(Id=N) / Zapata(Id=N) comparten nodo en la interfaz Z=Cota.
/// </para>
/// </summary>
internal static class SyncEscenaService
{
    // ---- Paleta de colores WPF Media (compartida estática) ----
    //
    // LineGeometryModel3D.Color es DependencyProperty con tipo
    // System.Windows.Media.Color (struct WPF). Instancias precreadas:
    // cada Element3D pinta su Color por valor, así que las constantes
    // evitan asignaciones repetidas. Paleta alineada al briefing del
    // usuario (Fase 3D-I2 / Fase 3D-I3).
    internal static readonly Color ColorViga    = Color.FromArgb(0xFF, 0x4A, 0x90, 0xE2);   // azul estructural
    internal static readonly Color ColorColumna = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0);   // gris claro metálico
    internal static readonly Color ColorZapata  = Color.FromArgb(0xFF, 0xD2, 0x84, 0x4A);   // marrón suave
    internal static readonly Color ColorMuro    = Color.FromArgb(0xFF, 0x2E, 0xCC, 0x71);   // verde esmeralda

    /// <summary>Color amarillo dorado corporativo de alta visibilidad para el elemento seleccionado.</summary>
    internal static readonly Color ColorSeleccion = Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00);

    /// <summary>Grosor base (px) de las aristas wireframe en estado nominal.</summary>
    internal const double ThicknessNominal = 1.5;

    /// <summary>Grosor (px) de la arista resaltada — incrementado para alta visibilidad.</summary>
    internal const double ThicknessSeleccionado = 3.0;

    /// <summary>
    /// Construye las mallas wireframe del proyecto y las inserta en
    /// <paramref name="itemsEscena"/>. La colección debe estar vacía al
    /// llamar (responsabilidad del VM). El método es seguro de invocar
    /// desde el hilo de UI: el trabajo de geometría corre en
    /// <see cref="Task.Run"/> y el swap a la colección se hace por
    /// <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>.
    /// </summary>
    public static Task GenerarMallasProyectoAsync(
        Proyecto proyecto,
        ObservableElement3DCollection itemsEscena)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));
        if (itemsEscena is null) throw new ArgumentNullException(nameof(itemsEscena));

        return Task.Run(() =>
        {
            // ---- 1) PROYECCIÓN ANALÍTICA EN HILO SECUNDARIO ----
            // El builder vive en src.Core y es puro: construye el grafo
            // de nodos consolidados + aristas con tolerancia 1 mm.
            var grafo = GrafoProyectadoBuilder.Construir(proyecto);
            var mallasPendientes = new List<MallaPendiente>(grafo.Aristas.Count);

            // Indexación directa: GrafoProyectadoBuilder emite Ids de nodo
            // monotónicos crecientes desde 0, por lo que grafo.Nodos[id]
            // es siempre el nodo con Id=id. Esto evita un Dictionary o un
            // .First(n => n.Id == ...) en el caliente del bucle.
            var nodosPorId = (IReadOnlyList<NodoSintetico>)grafo.Nodos;

            foreach (var arista in grafo.Aristas)
            {
                var posI = nodosPorId[arista.IdInicio].Posicion;
                var posJ = nodosPorId[arista.IdFin].Posicion;

                var geom = new LineGeometry3D
                {
                    Positions = new Vector3Collection { posI, posJ },
                    Indices   = new IntCollection { 0, 1 },
                };

                mallasPendientes.Add(new MallaPendiente(
                    Tag: arista.Elemento,
                    Geometria: geom,
                    Color: ColorPorTipo(arista.Elemento.Tipo)));
            }

            // ---- 2) SWAP AL HILO DE UI ----
            // La instanciación de LineGeometryModel3D y el Add a la
            // ObservableElement3DCollection tienen afinidad estricta al hilo
            // de UI (ambos heredan de DependencyObject de WPF).
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                // Smoke test / xUnit sin Application: no hay UI thread; se
                // ignora la inyección visual. El cálculo geométrico previo
                // sigue siendo verificable.
                return;
            }

            dispatcher.Invoke(() =>
            {
                foreach (var malla in mallasPendientes)
                {
                    var elemento = new LineGeometryModel3D
                    {
                        Geometry  = malla.Geometria,
                        Color     = malla.Color,
                        Thickness = ThicknessNominal,
                        Tag       = malla.Tag,
                    };
                    itemsEscena.Add(elemento);
                }
            }, DispatcherPriority.Background);
        });
    }

    // ===================================================================
    // PALETA CROMÁTICA
    // ===================================================================

    /// <summary>
    /// Devuelve el <see cref="Color"/> WPF correspondiente al tipo de
    /// elemento estructural. Mantiene la consistencia de la paleta entre
    /// el viewport 3D y cualquier futura leyenda de la UI.
    /// </summary>
    internal static Color ColorPorTipo(TipoElemento tipo) => tipo switch
    {
        TipoElemento.Viga    => ColorViga,
        TipoElemento.Columna => ColorColumna,
        TipoElemento.Zapata  => ColorZapata,
        TipoElemento.Muro    => ColorMuro,
        _                    => ColorColumna,   // fallback defensivo
    };

    // ===================================================================
    // ACTUALIZACIÓN VISUAL EN CALIENTE (Fase 3D-I3 — Selección)
    // ===================================================================

    /// <summary>
    /// Aplica el resaltado cromático del elemento seleccionado sobre la
    /// escena 3D existente sin reconstruir ninguna geometría. Itera la
    /// colección visible, lee el <see cref="LineGeometryModel3D.Tag"/>
    /// de cada modelo, y muta sólo <see cref="LineGeometryModel3D.Color"/>
    /// y <see cref="LineGeometryModel3D.Thickness"/>:
    /// <list type="bullet">
    ///   <item>Modelo con <c>Tag == seleccionado</c> →
    ///   <see cref="ColorSeleccion"/> (#FFFFD700) + thickness 3.0.</item>
    ///   <item>Resto de modelos → su color nominal por tipo + thickness 1.5
    ///   (restauración del estado base por si venían de un resaltado
    ///   anterior).</item>
    /// </list>
    ///
    /// <para>
    /// El método es seguro de invocar repetidamente: la mutación es
    /// idempotente para el modelo correcto. Debe ejecutarse en el hilo de
    /// UI (los <see cref="Element3D"/> son <c>DependencyObject</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Restricción crítica</b> (Plan Maestro): bajo ningún concepto
    /// invoca a <see cref="GrafoProyectadoBuilder"/> ni recrea las
    /// mallas — el cambio visual debe ser O(N) sobre la colección
    /// existente, sin upload de buffers a GPU.
    /// </para>
    /// </summary>
    public static void ActualizarResaltadoVisual(
        ObservableElement3DCollection? itemsEscena, DomainKey? seleccionado)
    {
        if (itemsEscena is null) return;

        foreach (var elemento in itemsEscena)
        {
            if (elemento is not LineGeometryModel3D linea) continue;
            if (linea.Tag is not DomainKey tag) continue;

            bool esSeleccionado = seleccionado.HasValue && tag.Equals(seleccionado.Value);
            if (esSeleccionado)
            {
                linea.Color     = ColorSeleccion;
                linea.Thickness = ThicknessSeleccionado;
            }
            else
            {
                linea.Color     = ColorPorTipo(tag.Tipo);
                linea.Thickness = ThicknessNominal;
            }
        }
    }

    // ===================================================================
    // TIPOS AUXILIARES (privados al servicio)
    // ===================================================================

    /// <summary>
    /// Bundle de datos thread-agnóstico que el hilo de trabajo pasa al
    /// hilo de UI para construir el <see cref="LineGeometryModel3D"/>.
    /// El <c>Tag</c> es un <see cref="DomainKey"/> proveniente del
    /// grafo de <c>src.Core</c>; sustituye a la <c>EtiquetaElemento3D</c>
    /// local de la Iteración 1 (ya eliminada).
    /// </summary>
    private readonly record struct MallaPendiente(
        DomainKey Tag,
        LineGeometry3D Geometria,
        Color Color);
}

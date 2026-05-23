using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;          // Color de WPF (consumido por LineGeometryModel3D.Color)
using System.Windows.Threading;
using HelixToolkit;                  // Vector3Collection, IntCollection
using HelixToolkit.SharpDX;          // LineGeometry3D
using HelixToolkit.Wpf.SharpDX;      // LineGeometryModel3D
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;
using NumericsVector3 = System.Numerics.Vector3;

namespace LosasPlus.ViewModels.Viewport3D;

/// <summary>
/// Servicio puro que sintetiza la geometría wireframe 3D del proyecto a
/// partir del modelo plano de persistencia v3 (Fase 3D-I1 del Plan Maestro
/// de Expansión 3D). Se ejecuta sobre <see cref="Task.Run"/> y delega la
/// instanciación de <see cref="Element3D"/> al hilo de UI vía
/// <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>.
///
/// <para>
/// <b>Estrategia de proyección espacial</b>:
/// <list type="bullet">
/// <item><b>Z global</b>: la cota del nivel sirve como base. Para un nivel
/// <c>n</c>, <c>Z_base = Niveles[0..n-1].Sum(Cota)</c> NO — el campo
/// <c>Nivel.Cota</c> ya almacena la elevación absoluta del piso, así que se
/// usa directamente.</item>
/// <item><b>X/Y de Muros</b>: tomados literalmente de
/// <see cref="Muro.PuntoInicio"/> y <see cref="Muro.PuntoFin"/> (coordenadas
/// del lienzo CAD 2D, en metros).</item>
/// <item><b>X/Y de Vigas/Columnas/Zapatas</b>: el modelo v3 no tiene
/// coordenadas globales para estas entidades; se distribuyen en una grilla
/// artificial separada por <see cref="EspaciadoGrilla"/> metros para
/// hacerlas visibles. La sintetización formal de coordenadas (Grafo Único
/// Coordinado con nodos compartidos) llega en Fase 3D-I2 vía
/// <c>GrafoProyectadoBuilder</c> en <c>src.Core</c>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Threading</b>: las colecciones <see cref="Vector3Collection"/> e
/// <see cref="IntCollection"/> son thread-agnósticas; la construcción de
/// <see cref="LineGeometry3D"/> ocurre en el <see cref="Task.Run"/>. La
/// instanciación de <see cref="LineGeometryModel3D"/> y su inserción en
/// la <see cref="ObservableElement3DCollection"/> bindeada se hace en el
/// hilo de UI vía <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>
/// con prioridad <c>Background</c>.
/// </para>
/// </summary>
internal static class SyncEscenaService
{
    /// <summary>
    /// Separación nominal entre vigas/columnas/zapatas dispuestas en la
    /// grilla artificial (X, Y). El valor se ajustará en Fase 3D-I2 cuando
    /// los nodos sintéticos del grafo proyectado provean coordenadas reales.
    /// </summary>
    private const double EspaciadoGrilla = 5.0;

    // ---- Paleta de colores WPF Media (compartida estática) ----
    //
    // LineGeometryModel3D.Color es DependencyProperty con tipo
    // System.Windows.Media.Color (struct WPF). Instancias precreadas:
    // cada Element3D pinta su Color por valor, así que las constantes
    // evitan asignaciones repetidas.
    private static readonly Color ColorViga    = Color.FromArgb(0xFF, 0x0E, 0x7D, 0xB8);   // azul acero
    private static readonly Color ColorColumna = Color.FromArgb(0xFF, 0xC2, 0x8A, 0x1E);   // ámbar
    private static readonly Color ColorZapata  = Color.FromArgb(0xFF, 0xD9, 0x76, 0x06);   // naranja
    private static readonly Color ColorMuro    = Color.FromArgb(0xFF, 0xBD, 0xBD, 0xBD);   // gris claro

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
            // ---- 1) PASADA EN HILO SECUNDARIO ----
            // Computar todas las geometrías como pares (Tag, LineGeometry3D, Color).
            var mallasPendientes = new List<MallaPendiente>(64);

            foreach (var edificio in proyecto.Edificios)
            {
                foreach (var nivel in edificio.Niveles)
                {
                    // Z global = la cota almacenada en el nivel (elevación absoluta).
                    double zBase = nivel.Cota;
                    // Altura del piso: aproximada por la altura típica de columnas
                    // del nivel; en su defecto, 3.0 m. Se usa para extruir muros.
                    double alturaNivel = AlturaTipicaNivel(nivel);

                    ProyectarMuros(nivel, zBase, alturaNivel, mallasPendientes);
                    ProyectarColumnas(nivel, zBase, mallasPendientes);
                    ProyectarVigas(nivel, zBase + alturaNivel, mallasPendientes);
                    ProyectarZapatas(nivel, zBase, mallasPendientes);
                }
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
                        Geometry = malla.Geometria,
                        Color    = malla.Color,
                        Thickness = 1.5,
                        Tag = malla.Tag,
                    };
                    itemsEscena.Add(elemento);
                }
            }, DispatcherPriority.Background);
        });
    }

    // ===================================================================
    // PROYECTORES POR TIPO DE ELEMENTO
    // ===================================================================

    /// <summary>
    /// Proyecta los muros del nivel. Únicos elementos con coordenadas X/Y
    /// reales en el v3 (vía <see cref="PuntoCad"/>). Se extruyen del z-base
    /// del nivel hasta <c>zBase + alturaMuro</c> y se renderizan como las
    /// cuatro aristas verticales del prisma + el rectángulo del contorno
    /// superior.
    /// </summary>
    private static void ProyectarMuros(
        Nivel nivel, double zBase, double alturaNivel, List<MallaPendiente> destino)
    {
        foreach (var sistema in nivel.Sistemas)
        {
            foreach (var muro in sistema.Muros)
            {
                double zTop = zBase + (muro.Altura > 0 ? muro.Altura : alturaNivel);

                var p0 = new NumericsVector3((float)muro.PuntoInicio.X, (float)muro.PuntoInicio.Y, (float)zBase);
                var p1 = new NumericsVector3((float)muro.PuntoFin.X,    (float)muro.PuntoFin.Y,    (float)zBase);
                var p2 = new NumericsVector3((float)muro.PuntoFin.X,    (float)muro.PuntoFin.Y,    (float)zTop);
                var p3 = new NumericsVector3((float)muro.PuntoInicio.X, (float)muro.PuntoInicio.Y, (float)zTop);

                var geom = new LineGeometry3D
                {
                    Positions = new Vector3Collection { p0, p1, p2, p3 },
                    // 4 aristas del rectángulo vertical (eje del muro):
                    //   p0→p1 (base), p1→p2 (vertical j), p2→p3 (corona),
                    //   p3→p0 (vertical i).
                    Indices = new IntCollection { 0, 1, 1, 2, 2, 3, 3, 0 },
                };

                destino.Add(new MallaPendiente(
                    Tag: new EtiquetaElemento3D(TipoElemento.Muro, muro.Id),
                    Geometria: geom,
                    Color: ColorMuro));
            }
        }
    }

    /// <summary>
    /// Proyecta las columnas como un segmento vertical desde
    /// <c>(x_grid, y_grid, zBase)</c> hasta <c>(x_grid, y_grid, zBase + Altura)</c>.
    /// Las coordenadas X/Y vienen de una grilla artificial (limitación
    /// conocida de la primera iteración — corregida en Fase 3D-I2).
    /// </summary>
    private static void ProyectarColumnas(Nivel nivel, double zBase, List<MallaPendiente> destino)
    {
        int idx = 0;
        foreach (var columna in nivel.Columnas)
        {
            var (x, y) = PosicionEnGrilla(idx++);
            var p0 = new NumericsVector3((float)x, (float)y, (float)zBase);
            var p1 = new NumericsVector3((float)x, (float)y, (float)(zBase + columna.Altura));

            var geom = new LineGeometry3D
            {
                Positions = new Vector3Collection { p0, p1 },
                Indices   = new IntCollection { 0, 1 },
            };

            destino.Add(new MallaPendiente(
                Tag: new EtiquetaElemento3D(TipoElemento.Columna, columna.Id),
                Geometria: geom,
                Color: ColorColumna));
        }
    }

    /// <summary>
    /// Proyecta las vigas como un segmento horizontal a lo largo del eje X
    /// con longitud igual a la suma de <see cref="TramoViga.Longitud"/>.
    /// El segmento arranca en la posición de grilla y se extiende en +X.
    /// </summary>
    private static void ProyectarVigas(Nivel nivel, double zEje, List<MallaPendiente> destino)
    {
        int idx = 0;
        foreach (var viga in nivel.Vigas)
        {
            double longitudTotal = 0;
            foreach (var tramo in viga.Tramos) longitudTotal += tramo.Longitud;
            if (longitudTotal <= 0) longitudTotal = 6.0;   // viga sin tramos: fallback visible

            var (x0, y) = PosicionEnGrilla(idx++);
            var p0 = new NumericsVector3((float)x0,                  (float)y, (float)zEje);
            var p1 = new NumericsVector3((float)(x0 + longitudTotal), (float)y, (float)zEje);

            var geom = new LineGeometry3D
            {
                Positions = new Vector3Collection { p0, p1 },
                Indices   = new IntCollection { 0, 1 },
            };

            destino.Add(new MallaPendiente(
                Tag: new EtiquetaElemento3D(TipoElemento.Viga, viga.Id),
                Geometria: geom,
                Color: ColorViga));
        }
    }

    /// <summary>
    /// Proyecta las zapatas como las 12 aristas de un prisma rectangular
    /// centrado en su posición de grilla. La cara superior queda en
    /// <c>z = zBase</c> y la cara inferior en <c>z = zBase − EspesorH</c>.
    /// </summary>
    private static void ProyectarZapatas(Nivel nivel, double zBase, List<MallaPendiente> destino)
    {
        int idx = 0;
        foreach (var zapata in nivel.Zapatas)
        {
            var (cx, cy) = PosicionEnGrilla(idx++);
            double bx = zapata.Dimensiones.LargoB * 0.5;
            double by = zapata.Dimensiones.AnchoL * 0.5;
            double zTop = zBase;
            double zBot = zBase - zapata.Dimensiones.EspesorH;

            // 8 vértices del prisma. Convención: 0..3 = cara inferior;
            // 4..7 = cara superior. Orden cíclico antihorario visto desde +Z.
            var positions = new Vector3Collection
            {
                new((float)(cx - bx), (float)(cy - by), (float)zBot),  // 0
                new((float)(cx + bx), (float)(cy - by), (float)zBot),  // 1
                new((float)(cx + bx), (float)(cy + by), (float)zBot),  // 2
                new((float)(cx - bx), (float)(cy + by), (float)zBot),  // 3
                new((float)(cx - bx), (float)(cy - by), (float)zTop),  // 4
                new((float)(cx + bx), (float)(cy - by), (float)zTop),  // 5
                new((float)(cx + bx), (float)(cy + by), (float)zTop),  // 6
                new((float)(cx - bx), (float)(cy + by), (float)zTop),  // 7
            };

            // 12 aristas: 4 base + 4 corona + 4 verticales.
            var indices = new IntCollection
            {
                0, 1,  1, 2,  2, 3,  3, 0,   // base
                4, 5,  5, 6,  6, 7,  7, 4,   // corona
                0, 4,  1, 5,  2, 6,  3, 7,   // verticales
            };

            destino.Add(new MallaPendiente(
                Tag: new EtiquetaElemento3D(TipoElemento.Zapata, zapata.Id),
                Geometria: new LineGeometry3D { Positions = positions, Indices = indices },
                Color: ColorZapata));
        }
    }

    // ===================================================================
    // HELPERS
    // ===================================================================

    /// <summary>
    /// Distribuye un índice secuencial en una grilla 2D de paso fijo. Para
    /// índices 0..n: fila = idx / 8, columna = idx % 8 — produce una grilla
    /// 8×N suficientemente amplia para visualizar proyectos modestos. Esta
    /// distribución artificial desaparece en Fase 3D-I2 cuando el
    /// <c>GrafoProyectadoBuilder</c> aporte coordenadas reales sintetizadas.
    /// </summary>
    private static (double X, double Y) PosicionEnGrilla(int indice)
    {
        const int columnasPorFila = 8;
        int fila = indice / columnasPorFila;
        int col  = indice % columnasPorFila;
        return (col * EspaciadoGrilla, fila * EspaciadoGrilla);
    }

    /// <summary>
    /// Altura "típica" de un nivel para extruir muros sin altura propia
    /// declarada: la mediana de las alturas de columnas del nivel, o 3.0 m
    /// como fallback ingenieril cuando el nivel no tiene columnas.
    /// </summary>
    private static double AlturaTipicaNivel(Nivel nivel)
    {
        if (nivel.Columnas.Count == 0) return 3.0;
        double suma = 0;
        foreach (var c in nivel.Columnas) suma += c.Altura;
        return suma / nivel.Columnas.Count;
    }

    // ===================================================================
    // TIPOS AUXILIARES (privados al servicio)
    // ===================================================================

    /// <summary>
    /// Bundle de datos thread-agnóstico que el hilo de trabajo pasa al
    /// hilo de UI para construir el <see cref="LineGeometryModel3D"/>.
    /// </summary>
    private readonly record struct MallaPendiente(
        EtiquetaElemento3D Tag,
        LineGeometry3D Geometria,
        Color Color);
}

/// <summary>
/// Tipo de elemento estructural detrás de cada <see cref="Element3D"/> del
/// viewport — anti-cast genérico usado en hit-testing futuro (Fase 3D-I3).
/// </summary>
public enum TipoElemento
{
    Viga,
    Columna,
    Zapata,
    Muro,
}

/// <summary>
/// Etiqueta inyectada en <c>Element3D.Tag</c> para mapear de vuelta al objeto
/// de dominio sin necesidad de un <see cref="Dictionary{TKey, TValue}"/>
/// externo: el hit-test devuelve el modelo y el VM lee directamente su
/// <c>Tag</c> para resolver tipo + id de dominio.
/// </summary>
public readonly record struct EtiquetaElemento3D(TipoElemento Tipo, int Id);

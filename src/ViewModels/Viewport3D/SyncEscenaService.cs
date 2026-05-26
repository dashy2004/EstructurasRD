using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;          // Color de WPF (consumido por LineGeometryModel3D.Color)
using System.Windows.Threading;
using HelixToolkit;                  // Vector3Collection, IntCollection
using HelixToolkit.SharpDX;          // LineGeometry3D, Geometry3D
using HelixToolkit.Wpf.SharpDX;      // LineGeometryModel3D, MeshGeometryModel3D, PhongMaterial(s)
using LosasPlus.Grillas;          // GrillaEstructural, EjeNombrado (Módulo 2 Fase 3D-II)
using LosasPlus.Models;
using LosasPlus.Services;         // LayoutSolver (posicionamiento topológico de losas)
using LosasPlus.Topologia;
using HxColor4 = HelixToolkit.Maths.Color4;
using HxMeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using NumericsVector3 = System.Numerics.Vector3;

namespace LosasPlus.ViewModels.Viewport3D;

/// <summary>
/// Servicio puro que sintetiza la geometría tridimensional del proyecto a
/// partir del grafo estructural derivado en <c>src.Core</c>
/// (<see cref="GrafoProyectadoBuilder"/>). Desde la Fase 3D-I4 del Plan
/// Maestro la representación pasa de wireframe a <b>sólido extruido</b>
/// usando <see cref="MeshGeometryModel3D"/> con cajas 3D orientadas por
/// los ejes locales CSI.
///
/// <para>
/// <b>Construcción manual de mallas</b>: por la ambigüedad de namespaces
/// entre <c>HelixToolkit.Geometry.MeshGeometry3D</c> (lo que produce el
/// <c>MeshBuilder</c>) y <c>HelixToolkit.SharpDX.MeshGeometry3D</c> (lo
/// que consume <see cref="MeshGeometryModel3D.Geometry"/>), las mallas
/// se construyen vértice-por-vértice con <see cref="Vector3Collection"/>
/// e <see cref="IntCollection"/> de SharpDX. Una caja se modela con 8
/// vértices y 12 triángulos (24 vértices si quisiéramos normales planas
/// por cara — para wireframe-solid suave con normales suavizadas, 8
/// vértices son suficientes).
/// </para>
///
/// <para>
/// <b>Pipeline</b>:
/// <list type="number">
/// <item>El motor analítico <see cref="GrafoProyectadoBuilder.Construir"/>
/// produce un <see cref="IGrafoEstructural"/> inmutable con nodos
/// consolidados a tolerancia de 1 mm.</item>
/// <item>Por cada <see cref="AristaSintetica"/> se construye una caja 3D
/// orientada al Eje1 del miembro con sección B×H (peralte ×
/// ancho).</item>
/// <item>Los muros siguen siendo wireframe en Parte A (sólido en Parte B).</item>
/// <item>El swap a la <see cref="ObservableElement3DCollection"/> bindeada
/// al <c>Viewport3DX</c> se realiza en el hilo de UI vía
/// <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>.</item>
/// </list>
/// </para>
/// </summary>
internal static class SyncEscenaService
{
    /// <summary>Sección de fallback B = H = 0.30 m para elementos sin dimensiones explícitas.</summary>
    private const float SeccionFallbackM = 0.30f;

    // ---- Paleta de colores WPF Media (compartida estática) ----
    internal static readonly Color ColorViga    = Color.FromArgb(0xFF, 0x4A, 0x90, 0xE2);   // azul estructural
    internal static readonly Color ColorColumna = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0);   // gris claro metálico
    internal static readonly Color ColorZapata  = Color.FromArgb(0xFF, 0xD2, 0x84, 0x4A);   // marrón suave
    internal static readonly Color ColorMuro    = Color.FromArgb(0xFF, 0x2E, 0xCC, 0x71);   // verde esmeralda
    // Losas (Módulo 1 Fase 3D-II): alpha 0x66 (40%) para que vigas y muros
    // bajo la losa sigan visibles. Gris azulado suave para que no compita
    // visualmente con los miembros estructurales.
    internal static readonly Color ColorLosa    = Color.FromArgb(0x66, 0xB0, 0xC0, 0xD0);
    // Grillas (Módulo 2 Fase 3D-II): gris tenue para no competir con miembros.
    internal static readonly Color ColorGrilla  = Color.FromArgb(0x99, 0x90, 0x90, 0x90);
    internal static readonly Color ColorSeleccion = Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00); // oro alta visibilidad

    // ---- Constantes wireframe (muros — Parte A; pasan a mesh en Parte B) ----
    internal const double ThicknessNominal = 1.5;
    internal const double ThicknessSeleccionado = 3.0;

    // ---- Materiales Phong precreados (compartidos estáticos) ----
    internal static readonly PhongMaterial MaterialViga      = CrearMaterial(ColorViga,      "MatViga");
    internal static readonly PhongMaterial MaterialColumna   = CrearMaterial(ColorColumna,   "MatColumna");
    internal static readonly PhongMaterial MaterialZapata    = CrearMaterial(ColorZapata,    "MatZapata");
    internal static readonly PhongMaterial MaterialMuro      = CrearMaterial(ColorMuro,      "MatMuro");
    internal static readonly PhongMaterial MaterialLosa      = CrearMaterial(ColorLosa,      "MatLosa");
    internal static readonly PhongMaterial MaterialSeleccion = CrearMaterial(ColorSeleccion, "MatSelec");
    // Cursor guía 3D (Módulo 2 Parte B Fase 3D-II): oro semitransparente
    // (alpha 50%) para que el ingeniero vea el cubo indicador SIN tapar
    // la geometría que está debajo. Construido derivando del color oro
    // existente con alpha reducido.
    internal static readonly PhongMaterial MaterialCursorGuia = CrearMaterial(
        Color.FromArgb(0x80, ColorSeleccion.R, ColorSeleccion.G, ColorSeleccion.B),
        "MatCursorGuia");

    // Cintas de diagramas estructurales (Fase 3D-I4 Parte B). Tres
    // estados cromáticos según el ratio D/C del miembro. Material con
    // alta auto-iluminación (emissive ≈ diffuse) para que la cinta sea
    // visible incluso en zonas de penumbra de la escena.
    private static readonly Color ColorDiagramaConforme = Color.FromArgb(0xCC, 0x2E, 0xCC, 0x71); // verde 80%
    private static readonly Color ColorDiagramaAlerta   = Color.FromArgb(0xCC, 0xF5, 0xB0, 0x41); // ámbar 80%
    private static readonly Color ColorDiagramaCritico  = Color.FromArgb(0xCC, 0xE7, 0x4C, 0x3C); // rojo  80%

    internal static readonly PhongMaterial MaterialDiagramaConforme = CrearMaterialEmisivo(ColorDiagramaConforme, "MatDiagOk");
    internal static readonly PhongMaterial MaterialDiagramaAlerta   = CrearMaterialEmisivo(ColorDiagramaAlerta,   "MatDiagAlerta");
    internal static readonly PhongMaterial MaterialDiagramaCritico  = CrearMaterialEmisivo(ColorDiagramaCritico,  "MatDiagErr");

    /// <summary>
    /// Material Phong con componente emisiva alta — usado para las cintas
    /// de diagramas estructurales (M / V). El emissive ≈ diffuse hace que
    /// la cinta se vea con su color saturado independientemente de la
    /// iluminación de la escena, lo que la diferencia visualmente de la
    /// estructura sólida adyacente.
    /// </summary>
    private static PhongMaterial CrearMaterialEmisivo(Color color, string nombre) => new()
    {
        Name              = nombre,
        DiffuseColor      = ColorAFloat4(color),
        EmissiveColor     = ColorAFloat4(MultiplicarColor(color, 0.75f)),
        AmbientColor      = ColorAFloat4(MultiplicarColor(color, 0.30f)),
        SpecularColor     = new HxColor4(0f, 0f, 0f, 1f),
        SpecularShininess = 4f,
    };

    private static PhongMaterial CrearMaterial(Color color, string nombre) => new()
    {
        Name              = nombre,
        DiffuseColor      = ColorAFloat4(color),
        AmbientColor      = ColorAFloat4(MultiplicarColor(color, 0.40f)),
        SpecularColor     = new HxColor4(0.25f, 0.25f, 0.25f, 1f),
        SpecularShininess = 16f,
    };

    private static HxColor4 ColorAFloat4(Color c) =>
        new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    private static Color MultiplicarColor(Color c, float k)
    {
        byte r = (byte)Math.Clamp(c.R * k, 0, 255);
        byte g = (byte)Math.Clamp(c.G * k, 0, 255);
        byte b = (byte)Math.Clamp(c.B * k, 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    /// <summary>
    /// Construye las mallas sólidas del proyecto y las inserta en
    /// <paramref name="itemsEscena"/>. Patrón idéntico a Fases previas:
    /// <see cref="Task.Run"/> para geometría thread-agnóstica + swap a UI
    /// vía <see cref="Dispatcher.Invoke(Action, DispatcherPriority)"/>.
    /// </summary>
    public static Task GenerarMallasProyectoAsync(
        Proyecto proyecto,
        ObservableElement3DCollection itemsEscena,
        ModoDiagrama3D modoDiagrama = ModoDiagrama3D.Ninguno)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));
        if (itemsEscena is null) throw new ArgumentNullException(nameof(itemsEscena));

        return Task.Run(() =>
        {
            // ---- 1) PROYECCIÓN ANALÍTICA EN HILO SECUNDARIO ----
            var grafo = GrafoProyectadoBuilder.Construir(proyecto);
            var pendientes = new List<ElementoPendiente>(grafo.Aristas.Count);
            var nodosPorId = (IReadOnlyList<NodoSintetico>)grafo.Nodos;

            foreach (var arista in grafo.Aristas)
            {
                var posI = nodosPorId[arista.IdInicio].Posicion;
                var posJ = nodosPorId[arista.IdFin].Posicion;
                var dims = DimensionesDeSeccion(proyecto, arista.Elemento);

                switch (arista.Elemento.Tipo)
                {
                    case TipoElemento.Viga:
                    case TipoElemento.Columna:
                        pendientes.Add(ElementoPendiente.ParaMesh(
                            arista.Elemento,
                            ConstruirCajaOrientada(posI, posJ, arista.Ejes, dims.B, dims.H),
                            MaterialPorTipo(arista.Elemento.Tipo)));
                        break;

                    case TipoElemento.Zapata:
                        pendientes.Add(ElementoPendiente.ParaMesh(
                            arista.Elemento,
                            ConstruirCajaAxisAligned(posI, posJ, dims.B, dims.H),
                            MaterialPorTipo(arista.Elemento.Tipo)));
                        break;

                    case TipoElemento.Muro:
                        // Los muros se procesan en un bucle separado más
                        // abajo — el builder emite 4 aristas por muro
                        // (Fase 3D-I2) pero aquí sólo queremos UN prisma
                        // por muro. La inmutabilidad del muro queda
                        // garantizada porque sólo leemos el dominio.
                        continue;
                }
            }

            // Bucle independiente para muros: extrusión 3D del prisma
            // rectangular (L × t × H) por cada muro del dominio (Fase 3D-I4
            // Parte B). Garantiza una sola malla por muro independientemente
            // de cuántas aristas auxiliares emita el grafo proyectado.
            foreach (var ed in proyecto.Edificios)
            {
                foreach (var ni in ed.Niveles)
                {
                    double zBase = ni.Cota;
                    foreach (var sis in ni.Sistemas)
                    {
                        foreach (var muro in sis.Muros)
                        {
                            pendientes.Add(ElementoPendiente.ParaMesh(
                                new DomainKey(TipoElemento.Muro, muro.Id),
                                ConstruirPrismaMuro(muro, zBase),
                                MaterialMuro));
                        }
                    }
                }
            }

            // Losas (Módulo 1 Fase 3D-II): prisma rectangular extruido
            // posicionado por LayoutSolver — el MISMO solver que el
            // editor 2D de CadCanvasHost. La losa cuelga debajo de la
            // cota del nivel (la superficie superior queda exactamente
            // sobre Z = Cota). Hit-testing vía Tag(TipoElemento.Losa, Id).
            foreach (var ed in proyecto.Edificios)
            {
                foreach (var ni in ed.Niveles)
                {
                    double zNivel = ni.Cota;
                    foreach (var sis in ni.Sistemas)
                    {
                        if (sis.Losas.Count == 0) continue;
                        var layout = LayoutSolver.Solve(sis);
                        foreach (var p in layout.Placements)
                        {
                            pendientes.Add(ElementoPendiente.ParaMesh(
                                new DomainKey(TipoElemento.Losa, p.Id),
                                ConstruirPrismaLosa(p, zNivel),
                                MaterialLosa));
                        }
                    }
                }
            }

            // Grillas estructurales (Módulo 2 Fase 3D-II): líneas grises +
            // etiquetas billboard A/B/C × 1/2/3 sobre el plano de cada nivel.
            // El usuario las usa como referencia visual y, en modo de
            // creación, el cursor 3D hace snap magnético a sus intersecciones.
            foreach (var ed in proyecto.Edificios)
            {
                if (ed.Grillas.EstaVacia) continue;
                var ext = CalcularExtensionGrilla(ed.Grillas);
                foreach (var ni in ed.Niveles)
                {
                    foreach (var pendiente in ConstruirGrillaParaNivel(ed.Grillas, ni.Cota, ext))
                        pendientes.Add(pendiente);
                }
            }

            // Cintas de diagrama 3D por viga (Liga E paso E4 — antes:
            // Fase 3D-I4 Parte B placeholder con parábola).
            // Sólo se generan si modoDiagrama != Ninguno. El perfil real
            // se computa vía VigaContinuaEngine.Resolver y se normaliza al
            // máximo absoluto del campo seleccionado (M/V/δ). Si el motor
            // falla o la viga es inestable, fallback a la parábola
            // simétrica (placeholder histórico).
            if (modoDiagrama != ModoDiagrama3D.Ninguno)
            {
                foreach (var arista in grafo.Aristas)
                {
                    if (arista.Elemento.Tipo != TipoElemento.Viga) continue;
                    var posI = nodosPorId[arista.IdInicio].Posicion;
                    var posJ = nodosPorId[arista.IdFin].Posicion;
                    var perfil = ExtraerPerfilNormalizado(
                        proyecto, arista.Elemento.Id, modoDiagrama);
                    var cinta = ConstruirCintaMomento(posI, posJ, arista.Ejes, perfil);
                    if (cinta is null) continue;
                    pendientes.Add(ElementoPendiente.ParaCinta(cinta, MaterialDiagramaConforme));
                }
            }

            // ---- 2) SWAP AL HILO DE UI ----
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                // Smoke test / xUnit sin Application: no hay UI thread.
                return;
            }

            dispatcher.Invoke(() =>
            {
                foreach (var p in pendientes)
                {
                    if (p.Mesh is not null && p.Material is not null)
                    {
                        var modelo = new MeshGeometryModel3D
                        {
                            Geometry              = p.Mesh,
                            Material              = p.Material,
                            Tag                   = p.Tag,
                            // El winding de las cajas construidas
                            // manualmente es CCW visto desde el exterior,
                            // así que FrontCounterClockwise=true combina
                            // con CullMode.Back para mostrar las caras
                            // exteriores sin transparencias falsas.
                            CullMode              = global::SharpDX.Direct3D11.CullMode.Back,
                            FrontCounterClockwise = true,
                        };
                        itemsEscena.Add(modelo);
                    }
                    else if (p.Linea is not null)
                    {
                        var modelo = new LineGeometryModel3D
                        {
                            Geometry  = p.Linea,
                            Color     = p.LineColor,
                            Thickness = ThicknessNominal,
                            Tag       = p.Tag,
                        };
                        itemsEscena.Add(modelo);
                    }
                    else if (p.BillboardTexto is not null && p.BillboardPos.HasValue)
                    {
                        // Etiqueta billboard 3D (Módulo 2 Fase 3D-II): el texto
                        // siempre orientado a la cámara. Sin Tag — no
                        // seleccionable; sirve como rotulación pura.
                        // HelixToolkit.SharpDX.TextInfo (no System.Globalization).
                        var billboard = new BillboardSingleText3D
                        {
                            TextInfo = new HelixToolkit.SharpDX.TextInfo(
                                p.BillboardTexto, p.BillboardPos.Value),
                        };
                        var modelo = new BillboardTextModel3D
                        {
                            Geometry = billboard,
                            Tag      = null,
                        };
                        itemsEscena.Add(modelo);
                    }
                }
            }, DispatcherPriority.Background);
        });
    }

    // ===================================================================
    // EXTRUSIÓN DE MUROS (Fase 3D-I4 Parte B)
    // ===================================================================

    /// <summary>
    /// Prisma 3D rectangular para un muro: extrusión vertical del segmento
    /// en planta (<c>PuntoInicio → PuntoFin</c>) por su espesor real
    /// (<c>Muro.Espesor</c>) y altura (<c>Muro.Altura</c> o 3 m por
    /// defecto). La base se ubica en Z = <c>Nivel.Cota</c>; la corona en
    /// Z + Altura.
    ///
    /// <para>
    /// Los 8 vértices se calculan desplazando ortogonalmente el eje del
    /// muro una distancia ±t/2 en el plano XY usando el vector normal
    /// perpendicular en planta, y luego proyectando verticalmente.
    /// </para>
    /// </summary>
    internal static HxMeshGeometry3D ConstruirPrismaMuro(LosasPlus.Models.Cad.Muro muro, double zBase)
    {
        double altura = muro.Altura > 0 ? muro.Altura : 3.0;
        double espesor = muro.Espesor > 0 ? muro.Espesor : 0.15;

        // Vector director del eje del muro en planta.
        double dx = muro.PuntoFin.X - muro.PuntoInicio.X;
        double dy = muro.PuntoFin.Y - muro.PuntoInicio.Y;
        double longitud = Math.Sqrt(dx * dx + dy * dy);
        if (longitud < 1e-6) longitud = 0.001;

        // Vector unitario longitudinal y normal (rotación +90° en plano).
        double ux = dx / longitud;
        double uy = dy / longitud;
        double nx = -uy;
        double ny =  ux;

        double offsetX = nx * (espesor * 0.5);
        double offsetY = ny * (espesor * 0.5);

        // 4 vértices de la base — orden cíclico antihorario visto desde +Z.
        var b0 = new NumericsVector3((float)(muro.PuntoInicio.X - offsetX), (float)(muro.PuntoInicio.Y - offsetY), (float)zBase);
        var b1 = new NumericsVector3((float)(muro.PuntoFin.X    - offsetX), (float)(muro.PuntoFin.Y    - offsetY), (float)zBase);
        var b2 = new NumericsVector3((float)(muro.PuntoFin.X    + offsetX), (float)(muro.PuntoFin.Y    + offsetY), (float)zBase);
        var b3 = new NumericsVector3((float)(muro.PuntoInicio.X + offsetX), (float)(muro.PuntoInicio.Y + offsetY), (float)zBase);

        double zTop = zBase + altura;
        var t0 = new NumericsVector3(b0.X, b0.Y, (float)zTop);
        var t1 = new NumericsVector3(b1.X, b1.Y, (float)zTop);
        var t2 = new NumericsVector3(b2.X, b2.Y, (float)zTop);
        var t3 = new NumericsVector3(b3.X, b3.Y, (float)zTop);

        var positions = new Vector3Collection { b0, b1, b2, b3, t0, t1, t2, t3 };

        // Normales suavizadas: cada vértice toma la dirección desde el
        // centro del prisma. Para un muro la diagonal es bastante asimétrica,
        // así que esto produce un sombreado plano-aproximado coherente.
        var centro = new NumericsVector3(
            (b0.X + b2.X) * 0.5f, (b0.Y + b2.Y) * 0.5f, (float)((zBase + zTop) * 0.5));
        var normals = new Vector3Collection(8);
        foreach (var v in positions)
        {
            var n = v - centro;
            float len = n.Length();
            normals.Add(len > 1e-6f ? n / len : new NumericsVector3(0f, 0f, 1f));
        }

        // 12 triángulos — 2 por cada una de las 6 caras del prisma.
        // Winding CCW visto desde el exterior (consistente con CullMode.Back +
        // FrontCounterClockwise=true del visor).
        var indices = new IntCollection
        {
            // Cara lateral 1 (b0→b1, normal -nx -ny aprox)
            0, 1, 5,   0, 5, 4,
            // Cara longitudinal exterior (b1→b2, normal +ux)
            1, 2, 6,   1, 6, 5,
            // Cara lateral 2 (b2→b3, normal +nx +ny)
            2, 3, 7,   2, 7, 6,
            // Cara longitudinal opuesta (b3→b0, normal -ux)
            3, 0, 4,   3, 4, 7,
            // Tapa superior (b → t)
            4, 5, 6,   4, 6, 7,
            // Base inferior (orden invertido para que la normal apunte -Z)
            0, 3, 2,   0, 2, 1,
        };

        return new HxMeshGeometry3D
        {
            Positions       = positions,
            Normals         = normals,
            TriangleIndices = indices,
        };
    }

    // ===================================================================
    // EXTRUSIÓN DE LOSAS (Módulo 1 Fase 3D-II)
    // ===================================================================

    /// <summary>
    /// Prisma rectangular axis-aligned para una <c>Losa</c> posicionada
    /// por <see cref="LayoutSolver"/>. El sólido se construye con 8
    /// vértices y 12 triángulos (winding CCW exterior, consistente con
    /// <c>CullMode.Back + FrontCounterClockwise=true</c> del visor).
    ///
    /// <para>
    /// Convención de cota: la <b>superficie superior</b> de la losa
    /// queda exactamente en <c>Z = zNivel</c>, la base en
    /// <c>Z = zNivel - Espesor</c>. La losa "cuelga" del nivel — su
    /// centro está a media altura de espesor por debajo de la cota.
    /// </para>
    ///
    /// <para>
    /// Las coordenadas (X, Y) en planta vienen del <c>Placement</c> del
    /// solver: esquina superior-izquierda en <c>(p.X, p.Y)</c>, ancho
    /// <c>p.Width = Losa.Lx</c>, alto <c>p.Height = Losa.Ly</c>. El
    /// sistema 2D del solver mapea directamente al sistema 3D global
    /// (X→X, Y→Y) — convención consistente con la proyección de muros
    /// en <see cref="GrafoProyectadoBuilder"/>.
    /// </para>
    /// </summary>
    /// <param name="placement">
    /// Placement del solver con (X, Y, Width, Height) en metros y
    /// referencia a la Losa para extraer <c>Espesor</c>.
    /// </param>
    /// <param name="zNivel">Cota del nivel (Z superior de la losa), en metros.</param>
    internal static HxMeshGeometry3D ConstruirPrismaLosa(
        LayoutSolver.Placement placement, double zNivel)
    {
        // Fallback defensivo: si el espesor es cero o negativo (losa
        // degenerada), usa 0.10 m para que el prisma sea visible.
        double espesor = placement.Losa.Espesor > 0 ? placement.Losa.Espesor : 0.10;

        // Centro del prisma en (X, Y, Z) — el (X, Y) cae al centroide
        // del rectángulo, el Z queda a media altura entre base y corona.
        var centro = new NumericsVector3(
            (float)(placement.X + placement.Width  * 0.5),
            (float)(placement.Y + placement.Height * 0.5),
            (float)(zNivel - espesor * 0.5));

        // Ejes alineados al sistema global — la losa no rota.
        var ex = new NumericsVector3(1f, 0f, 0f);
        var ey = new NumericsVector3(0f, 1f, 0f);
        var ez = new NumericsVector3(0f, 0f, 1f);

        // Mitades de cada dimensión.
        float hx = (float)(placement.Width  * 0.5);
        float hy = (float)(placement.Height * 0.5);
        float hz = (float)(espesor          * 0.5);

        return ConstruirCajaDesdeEjes(centro, ex, ey, ez, hx, hy, hz);
    }

    // ===================================================================
    // CINTAS DE DIAGRAMAS DE MOMENTOS 3D (Fase 3D-I4 Parte B)
    // ===================================================================

    /// <summary>Número de estaciones de control a lo largo del eje de la viga.</summary>
    private const int EstacionesCintaDiagrama = 12;

    /// <summary>
    /// Factor de escala visual del diagrama: la cinta se desplaza
    /// perpendicularmente al eje en magnitud
    /// <c>EscalaDiagramaM × valorNormalizado</c>. Mantenido como constante
    /// porque la fase es estética; cuando se cablee <c>VigaContinuaEngine</c>
    /// pasará a calibrarse contra el rango de momentos reales del proyecto.
    /// </summary>
    private const float EscalaDiagramaM = 0.75f;

    /// <summary>
    /// Construye una cinta 3D (quad strip) representativa del diagrama de
    /// momentos elásticos para una viga. Mientras no se cablee
    /// <see cref="LosasPlus.Vigas.VigaContinuaEngine"/>, usa un perfil
    /// parabólico <c>M(t) = 4·t·(1 − t)</c> (viga simplemente apoyada
    /// bajo carga distribuida uniforme) como proxy visual. La cinta se
    /// extiende perpendicularmente al miembro a lo largo del Eje2 local.
    /// </summary>
    /// <summary>
    /// Extrae el perfil normalizado [-1, +1] del campo seleccionado
    /// (Momento / Cortante / Deflexión) para la viga con
    /// <paramref name="idViga"/>, resolviéndola con
    /// <see cref="LosasPlus.Services.VigaContinuaEngine.Resolver"/>.
    /// Devuelve <c>null</c> si la viga no existe en el proyecto, está
    /// inestable, o el motor lanzó. El consumidor cae a la parábola
    /// histórica via fallback dentro de <see cref="ConstruirCintaMomento"/>.
    /// Liga E paso E4.
    /// </summary>
    private static IReadOnlyList<float>? ExtraerPerfilNormalizado(
        Proyecto proyecto, int idViga, ModoDiagrama3D modo)
    {
        try
        {
            // Buscar la viga por Id en todos los niveles.
            LosasPlus.Vigas.Viga? viga = null;
            foreach (var ed in proyecto.Edificios)
                foreach (var ni in ed.Niveles)
                    foreach (var v in ni.Vigas)
                        if (v.Id == idViga) { viga = v; goto encontrada; }
            encontrada:
            if (viga is null) return null;

            var res = LosasPlus.Services.VigaContinuaEngine.Resolver(viga, proyecto.Combinaciones);
            if (res.EsInestable || res.Envolvente is null) return null;
            var puntos = res.Envolvente.Puntos;
            if (puntos.Count < 2) return null;

            // Para cada punto, tomar el valor extremo (máximo absoluto) según el modo.
            double max = 0.0;
            var crudos = new double[puntos.Count];
            for (int i = 0; i < puntos.Count; i++)
            {
                var p = puntos[i];
                double val = modo switch
                {
                    ModoDiagrama3D.Momento   => System.Math.Abs(p.MomentoMaximo)  > System.Math.Abs(p.MomentoMinimo)  ? p.MomentoMaximo  : p.MomentoMinimo,
                    ModoDiagrama3D.Cortante  => System.Math.Abs(p.CortanteMaximo) > System.Math.Abs(p.CortanteMinimo) ? p.CortanteMaximo : p.CortanteMinimo,
                    ModoDiagrama3D.Deflexion => System.Math.Abs(p.DeflexionMaxima) > System.Math.Abs(p.DeflexionMinima) ? p.DeflexionMaxima : p.DeflexionMinima,
                    _                        => 0.0,
                };
                crudos[i] = val;
                double abs = System.Math.Abs(val);
                if (abs > max) max = abs;
            }
            if (max < 1e-9) return null;   // viga sin esfuerzos significativos

            // Normalizar al rango [-1, +1] dividiendo por el máximo absoluto.
            var perfil = new float[puntos.Count];
            for (int i = 0; i < puntos.Count; i++)
                perfil[i] = (float)(crudos[i] / max);
            return perfil;
        }
        catch
        {
            return null;   // fallback silencioso a la parábola
        }
    }

    internal static HxMeshGeometry3D? ConstruirCintaMomento(
        NumericsVector3 posI, NumericsVector3 posJ, EjesLocalesCSI ejes,
        IReadOnlyList<float>? perfilNormalizado = null)
    {
        var delta = posJ - posI;
        float longitud = delta.Length();
        if (longitud < 0.1f) return null;   // viga demasiado corta para la cinta

        var positions = new Vector3Collection(EstacionesCintaDiagrama * 2);
        var normals   = new Vector3Collection(EstacionesCintaDiagrama * 2);
        var indices   = new IntCollection((EstacionesCintaDiagrama - 1) * 6);

        // La normal de los quads de la cinta es el Eje3 (perpendicular al
        // plano 1-2). Todos los vértices comparten la misma normal — la
        // cinta es plana en el plano 1-2 local del miembro.
        var normalCinta = ejes.Eje3;

        for (int i = 0; i < EstacionesCintaDiagrama; i++)
        {
            float t = i / (float)(EstacionesCintaDiagrama - 1);
            // Liga E paso E4: si vino un perfil real desde la envolvente
            // del VigaContinuaEngine, samplear linealmente en las
            // EstacionesCintaDiagrama estaciones. Sino, fallback a la
            // parábola simétrica histórica (placeholder).
            float momentoNormalizado;
            if (perfilNormalizado is { Count: > 0 } perfil)
            {
                double tD = (double)i / (EstacionesCintaDiagrama - 1) * (perfil.Count - 1);
                int i0 = (int)System.Math.Floor(tD);
                int i1 = System.Math.Min(perfil.Count - 1, i0 + 1);
                float frac = (float)(tD - i0);
                momentoNormalizado = perfil[i0] * (1f - frac) + perfil[i1] * frac;
            }
            else
            {
                momentoNormalizado = 4f * t * (1f - t);
            }
            float desplazamiento = EscalaDiagramaM * momentoNormalizado;

            var posEje = posI + (t * longitud) * ejes.Eje1;
            // Convención: momento positivo desplaza la cinta en +Eje2.
            var posCinta = posEje + desplazamiento * ejes.Eje2;

            positions.Add(posEje);
            positions.Add(posCinta);
            normals.Add(normalCinta);
            normals.Add(normalCinta);

            if (i < EstacionesCintaDiagrama - 1)
            {
                int baseIdx = i * 2;
                // Dos triángulos por segmento — winding CCW visto desde +Eje3.
                indices.Add(baseIdx);
                indices.Add(baseIdx + 2);
                indices.Add(baseIdx + 3);
                indices.Add(baseIdx);
                indices.Add(baseIdx + 3);
                indices.Add(baseIdx + 1);
            }
        }

        return new HxMeshGeometry3D
        {
            Positions       = positions,
            Normals         = normals,
            TriangleIndices = indices,
        };
    }

    // ===================================================================
    // CONSTRUCCIÓN DE MALLAS (puramente analítica, thread-agnóstica)
    // ===================================================================

    /// <summary>
    /// Caja 3D centrada en el punto medio del segmento i→j y orientada a
    /// lo largo del <see cref="EjesLocalesCSI.Eje1"/>, con sección
    /// transversal B (ancho, alineada al Eje3) × H (peralte, alineada
    /// al Eje2). Construcción manual de 8 vértices + 12 triángulos +
    /// normales suavizadas — sin <c>MeshBuilder</c> para evitar el
    /// conflicto de namespaces entre el tipo de Geometry y SharpDX.
    /// </summary>
    internal static HxMeshGeometry3D ConstruirCajaOrientada(
        NumericsVector3 posI, NumericsVector3 posJ, EjesLocalesCSI ejes,
        float anchoB, float peralteH)
    {
        var centro = (posI + posJ) * 0.5f;
        float largo = (posJ - posI).Length();
        if (largo <= 1e-6f) largo = 0.1f;  // defensa contra segmentos degenerados

        // Mitades de las tres dimensiones, expresadas en el sistema local.
        float hx = largo * 0.5f;
        float hy = peralteH * 0.5f;
        float hz = anchoB * 0.5f;

        var ex = ejes.Eje1;   // longitudinal
        var ey = ejes.Eje2;   // peralte (típicamente "arriba" para horizontales)
        var ez = ejes.Eje3;   // ancho (lateral)

        return ConstruirCajaDesdeEjes(centro, ex, ey, ez, hx, hy, hz);
    }

    /// <summary>
    /// Caja 3D alineada a ejes globales — para zapatas. <paramref name="posI"/>
    /// es la base (Z menor) y <paramref name="posJ"/> el tope (Z mayor) del
    /// grafo. El lado horizontal se aplica simétricamente en X e Y;
    /// el espesor vertical viene de la diferencia <c>posJ.Z − posI.Z</c>.
    /// </summary>
    private static HxMeshGeometry3D ConstruirCajaAxisAligned(
        NumericsVector3 posI, NumericsVector3 posJ, float ladoXY, float espesorZ)
    {
        var centro = (posI + posJ) * 0.5f;
        float alturaZ = Math.Abs(posJ.Z - posI.Z);
        if (alturaZ < 1e-3f) alturaZ = espesorZ;

        var ex = new NumericsVector3(1f, 0f, 0f);
        var ey = new NumericsVector3(0f, 1f, 0f);
        var ez = new NumericsVector3(0f, 0f, 1f);

        float hx = ladoXY * 0.5f;
        float hy = ladoXY * 0.5f;
        float hz = alturaZ * 0.5f;

        return ConstruirCajaDesdeEjes(centro, ex, ey, ez, hx, hy, hz);
    }

    /// <summary>
    /// Construye una caja paramétrica: 8 vértices, 12 triángulos (2 por
    /// cada una de las 6 caras), normales suavizadas (promediadas en cada
    /// vértice por las 3 caras adyacentes). El winding de los triángulos
    /// es CCW visto desde el exterior, por lo que <c>CullMode.Back</c>
    /// con <c>FrontCounterClockwise=true</c> renderiza las caras
    /// exteriores correctamente.
    /// </summary>
    private static HxMeshGeometry3D ConstruirCajaDesdeEjes(
        NumericsVector3 centro,
        NumericsVector3 ex, NumericsVector3 ey, NumericsVector3 ez,
        float hx, float hy, float hz)
    {
        // 8 vértices de la caja, etiquetados según signos (s1, s2, s3) de
        // los desplazamientos a lo largo de ex/ey/ez.
        var v000 = centro + (-hx * ex) + (-hy * ey) + (-hz * ez);
        var v100 = centro + (+hx * ex) + (-hy * ey) + (-hz * ez);
        var v110 = centro + (+hx * ex) + (+hy * ey) + (-hz * ez);
        var v010 = centro + (-hx * ex) + (+hy * ey) + (-hz * ez);
        var v001 = centro + (-hx * ex) + (-hy * ey) + (+hz * ez);
        var v101 = centro + (+hx * ex) + (-hy * ey) + (+hz * ez);
        var v111 = centro + (+hx * ex) + (+hy * ey) + (+hz * ez);
        var v011 = centro + (-hx * ex) + (+hy * ey) + (+hz * ez);

        var positions = new Vector3Collection { v000, v100, v110, v010, v001, v101, v111, v011 };

        // Normales suavizadas (vértice = promedio de las 3 caras adyacentes,
        // normalizado). Esto evita las facetas duras del flat-shading.
        // Para una caja unitaria, la normal en cada esquina apunta hacia
        // ese octante: (sign_x, sign_y, sign_z) / sqrt(3).
        float invSqrt3 = 1.0f / MathF.Sqrt(3f);
        var normals = new Vector3Collection
        {
            (-ex - ey - ez) * invSqrt3,   // v000
            ( ex - ey - ez) * invSqrt3,   // v100
            ( ex + ey - ez) * invSqrt3,   // v110
            (-ex + ey - ez) * invSqrt3,   // v010
            (-ex - ey + ez) * invSqrt3,   // v001
            ( ex - ey + ez) * invSqrt3,   // v101
            ( ex + ey + ez) * invSqrt3,   // v111
            (-ex + ey + ez) * invSqrt3,   // v011
        };

        // 12 triángulos. Cada cara es un par de triángulos con winding CCW
        // visto desde el EXTERIOR de la caja:
        //   - Cara +X (v100, v110, v111, v101) — normal +ex.
        //   - Cara -X (v000, v010, v011, v001) — normal -ex.
        //   - Cara +Y (v110, v010, v011, v111) — normal +ey.
        //   - Cara -Y (v000, v100, v101, v001) — normal -ey.
        //   - Cara +Z (v001, v101, v111, v011) — normal +ez.
        //   - Cara -Z (v000, v100, v110, v010) — normal -ez.
        var indices = new IntCollection
        {
            // +X
            1, 2, 6,   1, 6, 5,
            // -X
            0, 4, 7,   0, 7, 3,
            // +Y
            3, 7, 6,   3, 6, 2,
            // -Y
            0, 1, 5,   0, 5, 4,
            // +Z
            4, 5, 6,   4, 6, 7,
            // -Z
            0, 3, 2,   0, 2, 1,
        };

        return new HxMeshGeometry3D
        {
            Positions       = positions,
            Normals         = normals,
            TriangleIndices = indices,
        };
    }

    // ===================================================================
    // RESOLUCIÓN DE DIMENSIONES POR DOMAIN KEY
    // ===================================================================

    /// <summary>
    /// Resuelve las dimensiones de sección (B, H) del elemento referenciado.
    /// Si el dominio v3 no aporta dimensiones, usa <see cref="SeccionFallbackM"/>.
    /// </summary>
    private static (float B, float H) DimensionesDeSeccion(Proyecto proyecto, DomainKey key)
    {
        switch (key.Tipo)
        {
            case TipoElemento.Columna:
                foreach (var ed in proyecto.Edificios)
                foreach (var ni in ed.Niveles)
                foreach (var co in ni.Columnas)
                    if (co.Id == key.Id)
                    {
                        float b = (float)Math.Max(co.Geometria.Base,    0.10);
                        float h = (float)Math.Max(co.Geometria.Peralte, 0.10);
                        return (b, h);
                    }
                break;

            case TipoElemento.Viga:
                foreach (var ed in proyecto.Edificios)
                foreach (var ni in ed.Niveles)
                foreach (var vi in ni.Vigas)
                    if (vi.Id == key.Id && vi.Tramos.Count > 0)
                    {
                        var s = vi.Tramos[0].Seccion;
                        if (s is not null)
                        {
                            float b = (float)Math.Max(s.Base,    0.10);
                            float h = (float)Math.Max(s.Peralte, 0.10);
                            return (b, h);
                        }
                    }
                break;

            case TipoElemento.Zapata:
                foreach (var ed in proyecto.Edificios)
                foreach (var ni in ed.Niveles)
                foreach (var za in ni.Zapatas)
                    if (za.Id == key.Id)
                    {
                        float lado    = (float)Math.Max(za.Dimensiones.LargoB,   0.20);
                        float espesor = (float)Math.Max(za.Dimensiones.EspesorH, 0.10);
                        return (lado, espesor);
                    }
                break;
        }
        return (SeccionFallbackM, SeccionFallbackM);
    }

    // ===================================================================
    // ACTUALIZACIÓN VISUAL EN CALIENTE (Fase 3D-I3 — Selección)
    // ===================================================================

    /// <summary>
    /// Aplica el resaltado del elemento seleccionado mutando sólo
    /// <see cref="MeshGeometryModel3D.Material"/> (sin reconstruir
    /// mallas) o <see cref="LineGeometryModel3D.Color"/>+
    /// <see cref="LineGeometryModel3D.Thickness"/>.
    /// </summary>
    public static void ActualizarResaltadoVisual(
        ObservableElement3DCollection? itemsEscena, DomainKey? seleccionado)
    {
        if (itemsEscena is null) return;

        foreach (var elemento in itemsEscena)
        {
            DomainKey? tag = elemento switch
            {
                MeshGeometryModel3D mesh  => mesh.Tag as DomainKey?,
                LineGeometryModel3D linea => linea.Tag as DomainKey?,
                _ => null,
            };
            if (tag is null) continue;
            bool esSeleccionado = seleccionado.HasValue && tag.Value.Equals(seleccionado.Value);

            switch (elemento)
            {
                case MeshGeometryModel3D meshModel:
                    meshModel.Material = esSeleccionado
                        ? MaterialSeleccion
                        : MaterialPorTipo(tag.Value.Tipo);
                    break;

                case LineGeometryModel3D lineModel:
                    lineModel.Color     = esSeleccionado ? ColorSeleccion : ColorPorTipo(tag.Value.Tipo);
                    lineModel.Thickness = esSeleccionado ? ThicknessSeleccionado : ThicknessNominal;
                    break;
            }
        }
    }

    // ===================================================================
    // PALETA / MATERIALES POR TIPO
    // ===================================================================

    internal static Color ColorPorTipo(TipoElemento tipo) => tipo switch
    {
        TipoElemento.Viga    => ColorViga,
        TipoElemento.Columna => ColorColumna,
        TipoElemento.Zapata  => ColorZapata,
        TipoElemento.Muro    => ColorMuro,
        TipoElemento.Losa    => ColorLosa,
        _                    => ColorColumna,
    };

    internal static PhongMaterial MaterialPorTipo(TipoElemento tipo) => tipo switch
    {
        TipoElemento.Viga    => MaterialViga,
        TipoElemento.Columna => MaterialColumna,
        TipoElemento.Zapata  => MaterialZapata,
        TipoElemento.Muro    => MaterialMuro,
        TipoElemento.Losa    => MaterialLosa,
        _                    => MaterialColumna,
    };

    // ===================================================================
    // TIPOS AUXILIARES (privados al servicio)
    // ===================================================================

    /// <summary>
    /// Bundle thread-agnóstico que el hilo de trabajo pasa al hilo de UI.
    /// Puede transportar una malla sólida con Tag (estructural —
    /// seleccionable), una malla auxiliar sin Tag (cinta de diagrama
    /// — no seleccionable) o una línea wireframe (legacy).
    /// </summary>
    private readonly record struct ElementoPendiente(
        DomainKey? Tag,
        HxMeshGeometry3D? Mesh,
        PhongMaterial? Material,
        LineGeometry3D? Linea,
        Color LineColor,
        NumericsVector3? BillboardPos = null,
        string? BillboardTexto = null)
    {
        public static ElementoPendiente ParaMesh(DomainKey tag, HxMeshGeometry3D mesh, PhongMaterial mat)
            => new(tag, mesh, mat, null, default);

        /// <summary>
        /// Malla auxiliar sin <see cref="DomainKey"/> (cinta de diagrama).
        /// El Tag <c>null</c> hace que <c>ActualizarResaltadoVisual</c>
        /// no la considere candidata a resaltado.
        /// </summary>
        public static ElementoPendiente ParaCinta(HxMeshGeometry3D mesh, PhongMaterial mat)
            => new(null, mesh, mat, null, default);

        public static ElementoPendiente ParaLinea(DomainKey tag, LineGeometry3D linea, Color color)
            => new(tag, null, null, linea, color);

        /// <summary>
        /// Línea wireframe sin DomainKey (grilla auxiliar — no
        /// seleccionable). Módulo 2 Fase 3D-II.
        /// </summary>
        public static ElementoPendiente ParaLineaSinTag(LineGeometry3D linea, Color color)
            => new(null, null, null, linea, color);

        /// <summary>
        /// Etiqueta billboard 3D (texto que siempre mira a la cámara) —
        /// usado para las marcas A/B/C × 1/2/3 de las grillas
        /// estructurales. Sin Tag (no seleccionable).
        /// </summary>
        public static ElementoPendiente ParaBillboard(NumericsVector3 posicion, string texto)
            => new(null, null, null, null, default, posicion, texto);
    }

    // ===================================================================
    // CURSOR GUÍA 3D (Módulo 2 Parte B Fase 3D-II)
    // ===================================================================

    /// <summary>
    /// Construye un cubo paramétrico centrado en el origen con
    /// <paramref name="ladoM"/> metros de lado — la geometría del
    /// cursor guía oro que el code-behind del visor 3D mueve por
    /// transformación para indicar al usuario dónde caería un click
    /// de creación. Reutiliza <c>ConstruirCajaDesdeEjes</c> con ejes
    /// axis-aligned y mitades iguales.
    /// </summary>
    /// <param name="ladoM">Lado del cubo, en metros. Default 0.25 m.</param>
    internal static HxMeshGeometry3D ConstruirCajaUnitaria(float ladoM = 0.25f)
    {
        float h = ladoM * 0.5f;
        return ConstruirCajaDesdeEjes(
            centro: new NumericsVector3(0f, 0f, 0f),
            ex:     new NumericsVector3(1f, 0f, 0f),
            ey:     new NumericsVector3(0f, 1f, 0f),
            ez:     new NumericsVector3(0f, 0f, 1f),
            hx: h, hy: h, hz: h);
    }

    /// <summary>
    /// Construye una geometría de línea de 2 vértices entre dos puntos
    /// del espacio 3D. Usada por el code-behind del visor 3D para
    /// renderizar el preview live de viga durante el flow de 2 clicks
    /// de <c>ModoHerramienta3D.CrearViga</c> (Liga B paso B3). El
    /// consumidor envuelve esta geometría en un
    /// <see cref="LineGeometryModel3D"/> ya instanciado en el XAML con
    /// material naranja.
    /// </summary>
    internal static LineGeometry3D ConstruirLineaProvisional(
        NumericsVector3 p1, NumericsVector3 p2)
    {
        var pts = new Vector3Collection { p1, p2 };
        return new LineGeometry3D
        {
            Positions = pts,
            Indices   = new IntCollection { 0, 1 },
        };
    }

    /// <summary>
    /// Color naranja saturado para el preview de viga durante el flow
    /// 2-click (Liga B B3). Coherente con el banner naranja de B5 — el
    /// usuario asocia naranja con "modo autoría 3D activo".
    /// </summary>
    internal static readonly Color ColorVigaPreview =
        Color.FromArgb(0xFF, 0xE6, 0x7E, 0x22);

    // ===================================================================
    // GRILLAS ESTRUCTURALES (Módulo 2 Fase 3D-II)
    // ===================================================================

    /// <summary>
    /// Margen lateral (m) por fuera del bounding box de la grilla — las
    /// líneas se extienden este margen extra en cada dirección para que
    /// los nodos de esquina queden visibles y las etiquetas no choquen
    /// con miembros estructurales del perímetro.
    /// </summary>
    private const double MargenExtensionGrillaM = 1.5;

    /// <summary>
    /// Calcula el bounding box de la grilla en el plano XY (rango de
    /// coordenadas en cada dirección) con un margen lateral añadido.
    /// El render extiende las líneas a estos extremos.
    /// </summary>
    private static (double xMin, double xMax, double yMin, double yMax)
        CalcularExtensionGrilla(GrillaEstructural grilla)
    {
        double xMin = 0, xMax = 0, yMin = 0, yMax = 0;
        bool primero = true;
        foreach (var ex in grilla.EjesX)
        {
            if (primero) { xMin = xMax = ex.PosicionMetros; primero = false; }
            else { if (ex.PosicionMetros < xMin) xMin = ex.PosicionMetros;
                   if (ex.PosicionMetros > xMax) xMax = ex.PosicionMetros; }
        }
        primero = true;
        foreach (var ey in grilla.EjesY)
        {
            if (primero) { yMin = yMax = ey.PosicionMetros; primero = false; }
            else { if (ey.PosicionMetros < yMin) yMin = ey.PosicionMetros;
                   if (ey.PosicionMetros > yMax) yMax = ey.PosicionMetros; }
        }
        // Margen lateral simétrico — las líneas se proyectan más allá del
        // último eje para que el cierre visual sea limpio.
        return (xMin - MargenExtensionGrillaM, xMax + MargenExtensionGrillaM,
                yMin - MargenExtensionGrillaM, yMax + MargenExtensionGrillaM);
    }

    /// <summary>
    /// Produce todos los <see cref="ElementoPendiente"/> de la grilla
    /// estructural sobre el plano <c>Z = zNivel</c>: una
    /// <see cref="LineGeometry3D"/> por cada EjeX (atravesando todo el
    /// rango Y) + una por cada EjeY (atravesando todo el rango X) + una
    /// etiqueta billboard al extremo de cada eje con su nombre
    /// (A/B/C × 1/2/3).
    ///
    /// <para>Tag <c>null</c> en todos los elementos — las grillas no
    /// son seleccionables.</para>
    /// </summary>
    private static IEnumerable<ElementoPendiente> ConstruirGrillaParaNivel(
        GrillaEstructural grilla, double zNivel,
        (double xMin, double xMax, double yMin, double yMax) ext)
    {
        // Líneas verticales (EjeX): atraviesan el rango Y completo.
        foreach (var ex in grilla.EjesX)
        {
            var pts = new Vector3Collection
            {
                new((float)ex.PosicionMetros, (float)ext.yMin, (float)zNivel),
                new((float)ex.PosicionMetros, (float)ext.yMax, (float)zNivel),
            };
            var linea = new LineGeometry3D
            {
                Positions = pts,
                Indices   = new IntCollection { 0, 1 },
            };
            yield return ElementoPendiente.ParaLineaSinTag(linea, ColorGrilla);
            // Etiqueta al extremo Y máximo (un poco más allá del margen).
            yield return ElementoPendiente.ParaBillboard(
                new NumericsVector3((float)ex.PosicionMetros, (float)(ext.yMax + 0.5), (float)zNivel),
                ex.Nombre);
        }
        // Líneas horizontales (EjeY): atraviesan el rango X completo.
        foreach (var ey in grilla.EjesY)
        {
            var pts = new Vector3Collection
            {
                new((float)ext.xMin, (float)ey.PosicionMetros, (float)zNivel),
                new((float)ext.xMax, (float)ey.PosicionMetros, (float)zNivel),
            };
            var linea = new LineGeometry3D
            {
                Positions = pts,
                Indices   = new IntCollection { 0, 1 },
            };
            yield return ElementoPendiente.ParaLineaSinTag(linea, ColorGrilla);
            // Etiqueta al extremo X mínimo (a la izquierda del margen).
            yield return ElementoPendiente.ParaBillboard(
                new NumericsVector3((float)(ext.xMin - 0.5), (float)ey.PosicionMetros, (float)zNivel),
                ey.Nombre);
        }
    }
}

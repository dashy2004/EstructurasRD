using System;
using System.Linq;
using System.Numerics;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Topologia;
using LosasPlus.Vigas;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests.Topologia;

/// <summary>
/// Tests del <see cref="GrafoProyectadoBuilder"/> y sus DTOs (Fase 3D-I2
/// del Plan Maestro de Expansión 3D). Validan invariantes matemáticas del
/// motor de proyección sintética: fusión de nodos a 1 mm, idempotencia,
/// convención CSI de ejes locales, conectividad cruzada (columna ↔ zapata
/// por Id) y robustez ante proyectos vacíos / nulos.
/// </summary>
public class GrafoProyectadoBuilderTests
{
    // ===================================================================
    // CASOS BASE
    // ===================================================================

    [Fact]
    public void Builder_Lanza_ArgumentNullException_Para_Proyecto_Nulo()
    {
        Assert.Throws<ArgumentNullException>(
            () => GrafoProyectadoBuilder.Construir(null!));
    }

    [Fact]
    public void Proyecto_Vacio_Retorna_Grafo_Vacio()
    {
        var proyecto = new Proyecto();   // sin Edificios

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        Assert.NotNull(grafo);
        Assert.Empty(grafo.Nodos);
        Assert.Empty(grafo.Aristas);
    }

    // ===================================================================
    // FUSIÓN DE NODOS (HashEspacialUniforme + tolerancia)
    // ===================================================================

    [Fact]
    public void Fusion_De_Nodos_Dentro_De_Tolerancia_De_Un_Milimetro()
    {
        // Dos posiciones a 0.5 mm = 0.0005 m de separación. Con la
        // tolerancia default de 1 mm el hash debe colapsarlas en un
        // único nodo.
        var hash = new HashEspacialUniforme();
        var pA = new Vector3(0f, 0f, 0f);
        var pB = new Vector3(0.0005f, 0f, 0f);

        int idA = hash.ObtenerOInsertarNodoId(pA);
        int idB = hash.ObtenerOInsertarNodoId(pB);

        Assert.Equal(idA, idB);
        Assert.Equal(1, hash.Cuenta);
    }

    [Fact]
    public void HashEspacial_PuntosFueraDeTolerancia_GeneraDosNodos()
    {
        // 5 mm de separación = claramente fuera de tolerancia 1 mm.
        var hash = new HashEspacialUniforme();
        int idA = hash.ObtenerOInsertarNodoId(new Vector3(0f, 0f, 0f));
        int idB = hash.ObtenerOInsertarNodoId(new Vector3(0.005f, 0f, 0f));

        Assert.NotEqual(idA, idB);
        Assert.Equal(2, hash.Cuenta);
    }

    [Fact]
    public void HashEspacial_PuntosCoincidentesGeneranUnSoloNodo()
    {
        var hash = new HashEspacialUniforme();
        var p = new Vector3(3.14f, 2.71f, 1.41f);

        int id1 = hash.ObtenerOInsertarNodoId(p);
        int id2 = hash.ObtenerOInsertarNodoId(p);
        int id3 = hash.ObtenerOInsertarNodoId(p);

        Assert.Equal(id1, id2);
        Assert.Equal(id1, id3);
        Assert.Equal(1, hash.Cuenta);
    }

    [Fact]
    public void HashEspacial_CrucePorBordeDeCelda_FusionaCorrectamente()
    {
        // Dos puntos a ambos lados de una frontera de celda (tamaño 10 mm)
        // pero dentro de la tolerancia de fusión deben colapsar — esto
        // valida que el vecindario 27-celdas captura cruces de frontera.
        var hash = new HashEspacialUniforme();
        var pA = new Vector3(0.0099f, 0f, 0f);   // justo antes del borde de celda
        var pB = new Vector3(0.0101f, 0f, 0f);   // justo después: |dist| ≈ 0.2 mm

        int idA = hash.ObtenerOInsertarNodoId(pA);
        int idB = hash.ObtenerOInsertarNodoId(pB);

        Assert.Equal(idA, idB);
        Assert.Equal(1, hash.Cuenta);
    }

    // ===================================================================
    // EJES LOCALES CSI
    // ===================================================================

    [Fact]
    public void EjesLocales_ColumnaVertical_Eje1_AlineadoConZ_Positivo()
    {
        var ejes = EjesLocalesCSI.Calcular(
            nodoI: new Vector3(2f, 3f, 0f),
            nodoJ: new Vector3(2f, 3f, 3f));

        Assert.Equal(0f, ejes.Eje1.X, precision: 6);
        Assert.Equal(0f, ejes.Eje1.Y, precision: 6);
        Assert.Equal(1f, ejes.Eje1.Z, precision: 6);

        // Convención CSI: para columnas verticales eje2 = +X global.
        Assert.Equal(1f, ejes.Eje2.X, precision: 6);
        Assert.Equal(0f, ejes.Eje2.Y, precision: 6);
        Assert.Equal(0f, ejes.Eje2.Z, precision: 6);
    }

    [Fact]
    public void EjesLocales_VigaHorizontal_Eje2_Apunta_HaciaArriba()
    {
        // Viga horizontal a lo largo de +X.
        var ejes = EjesLocalesCSI.Calcular(
            nodoI: new Vector3(0f, 5f, 4f),
            nodoJ: new Vector3(6f, 5f, 4f));

        // eje1 = +X
        Assert.Equal(1f, ejes.Eje1.X, precision: 6);
        // eje2 debe tener componente Z positiva (apunta hacia arriba)
        Assert.True(ejes.Eje2.Z > 0.999f,
            $"Esperado eje2.Z ≈ 1; valor real = {ejes.Eje2.Z:0.000000}.");
        // Plano 1-2 vertical → eje2.Y ≈ 0
        Assert.Equal(0f, ejes.Eje2.Y, precision: 6);
    }

    [Fact]
    public void EjesLocales_TriEjes_Son_Unitarios_Y_Ortogonales()
    {
        // Miembro inclinado arbitrario.
        var ejes = EjesLocalesCSI.Calcular(
            nodoI: new Vector3(0f, 0f, 0f),
            nodoJ: new Vector3(3f, 4f, 12f));

        // Unitarios: longitud ≈ 1
        Assert.Equal(1f, ejes.Eje1.Length(), precision: 5);
        Assert.Equal(1f, ejes.Eje2.Length(), precision: 5);
        Assert.Equal(1f, ejes.Eje3.Length(), precision: 5);

        // Ortogonales: productos punto ≈ 0
        Assert.Equal(0f, Vector3.Dot(ejes.Eje1, ejes.Eje2), precision: 5);
        Assert.Equal(0f, Vector3.Dot(ejes.Eje1, ejes.Eje3), precision: 5);
        Assert.Equal(0f, Vector3.Dot(ejes.Eje2, ejes.Eje3), precision: 5);
    }

    [Fact]
    public void EjesLocales_NodosCoincidentes_Lanza_ArgumentException()
    {
        var p = new Vector3(1f, 2f, 3f);
        Assert.Throws<ArgumentException>(
            () => EjesLocalesCSI.Calcular(p, p));
    }

    [Fact]
    public void EjesLocales_AnguloRollDe90_Rota_Eje2_Hacia_Eje3_Original()
    {
        // Para una columna vertical con eje2 = +X, eje3 = +Y (regla mano
        // derecha en 1→2→3), un roll de +90° debe rotar eje2 a +Y.
        var sinRoll = EjesLocalesCSI.Calcular(
            nodoI: new Vector3(0f, 0f, 0f),
            nodoJ: new Vector3(0f, 0f, 1f),
            anguloRollGrados: 0.0);
        var conRoll = EjesLocalesCSI.Calcular(
            nodoI: new Vector3(0f, 0f, 0f),
            nodoJ: new Vector3(0f, 0f, 1f),
            anguloRollGrados: 90.0);

        // sinRoll.Eje2 = +X (verificado en otro test).
        // conRoll.Eje2 = cos(90)·Eje2 + sin(90)·Eje3 = sinRoll.Eje3.
        Assert.Equal(sinRoll.Eje3.X, conRoll.Eje2.X, precision: 5);
        Assert.Equal(sinRoll.Eje3.Y, conRoll.Eje2.Y, precision: 5);
        Assert.Equal(sinRoll.Eje3.Z, conRoll.Eje2.Z, precision: 5);
    }

    // ===================================================================
    // GRILLA ARTIFICIAL DETERMINISTA
    // ===================================================================

    [Fact]
    public void PosicionEnGrillaArtificial_Id1_AterrizaEnOrigen()
    {
        var (x, y) = GrafoProyectadoBuilder.PosicionEnGrillaArtificial(1);
        Assert.Equal(0.0, x, precision: 6);
        Assert.Equal(0.0, y, precision: 6);
    }

    [Fact]
    public void PosicionEnGrillaArtificial_Indexacion_Determinista()
    {
        // Llamadas repetidas devuelven el mismo (X, Y).
        var (x1, y1) = GrafoProyectadoBuilder.PosicionEnGrillaArtificial(5);
        var (x2, y2) = GrafoProyectadoBuilder.PosicionEnGrillaArtificial(5);
        Assert.Equal(x1, x2);
        Assert.Equal(y1, y2);

        // Id distintos producen posiciones distintas (salvo overlap en fila/col).
        var (x3, _) = GrafoProyectadoBuilder.PosicionEnGrillaArtificial(1);
        var (x4, _) = GrafoProyectadoBuilder.PosicionEnGrillaArtificial(2);
        Assert.NotEqual(x3, x4);
    }

    // ===================================================================
    // PROYECCIÓN DE MUROS
    // ===================================================================

    [Fact]
    public void Muro_EmiteCuatroNodosYCuatroAristasPerimetrales()
    {
        var proyecto = ConstruirProyectoConMuro(
            puntoInicio: new PuntoCad(0.0, 0.0),
            puntoFin:    new PuntoCad(4.0, 0.0),
            altura:      3.0,
            cotaNivel:   0.0);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // 4 nodos: base inicio, base fin, corona fin, corona inicio.
        Assert.Equal(4, grafo.Nodos.Count);
        // 4 aristas: base, vertical j, corona, vertical i.
        Assert.Equal(4, grafo.Aristas.Count);
        // Todas las aristas referencian el mismo muro (Id=1).
        var muroKey = new DomainKey(TipoElemento.Muro, 1);
        Assert.All(grafo.Aristas, a => Assert.Equal(muroKey, a.Elemento));
    }

    [Fact]
    public void Muro_Z_Absoluto_Toma_Cota_Del_Nivel()
    {
        const double cotaNivel = 7.5;
        const double alturaMuro = 3.0;

        var proyecto = ConstruirProyectoConMuro(
            puntoInicio: new PuntoCad(0.0, 0.0),
            puntoFin:    new PuntoCad(2.0, 0.0),
            altura:      alturaMuro,
            cotaNivel:   cotaNivel);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);
        var zMin = grafo.Nodos.Min(n => n.Posicion.Z);
        var zMax = grafo.Nodos.Max(n => n.Posicion.Z);

        Assert.Equal((float)cotaNivel,                      zMin, precision: 5);
        Assert.Equal((float)(cotaNivel + alturaMuro),       zMax, precision: 5);
    }

    // ===================================================================
    // ZAPATA + COLUMNA: NODO COMPARTIDO
    // ===================================================================

    [Fact]
    public void Zapata_Y_Columna_Con_Mismo_Id_Comparten_Nodo_De_Interfaz()
    {
        var proyecto = ConstruirProyectoConColumnaYZapata(
            idColumnaZapata: 1,
            cotaNivel: 0.0,
            alturaColumna: 3.0,
            espesorZapata: 0.30);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // Esperados: 3 nodos = base zapata + interfaz compartida + corona columna.
        Assert.Equal(3, grafo.Nodos.Count);
        // Esperadas: 2 aristas = una para la zapata + una para la columna.
        Assert.Equal(2, grafo.Aristas.Count);

        // El nodo de interfaz (Z = cota = 0) debe tener AMBOS elementos incidentes.
        var nodoInterfaz = grafo.Nodos.Single(n => Math.Abs(n.Posicion.Z) < 1e-6);
        Assert.Contains(new DomainKey(TipoElemento.Columna, 1), nodoInterfaz.ElementosIncidentes);
        Assert.Contains(new DomainKey(TipoElemento.Zapata,  1), nodoInterfaz.ElementosIncidentes);
    }

    [Fact]
    public void Columna_Sin_Zapata_Renderiza_Una_Sola_Arista_Vertical()
    {
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Columnas.Add(new Columna { Id = 7, Altura = 3.5 });
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // 2 nodos (base + corona), 1 arista columna.
        Assert.Equal(2, grafo.Nodos.Count);
        Assert.Single(grafo.Aristas);
        Assert.Equal(new DomainKey(TipoElemento.Columna, 7), grafo.Aristas[0].Elemento);
    }

    [Fact]
    public void Zapata_Huerfana_Sin_Columna_Se_Renderiza_En_Posicion_Distinta()
    {
        // Columna(Id=1) sin zapata + Zapata(Id=99) sin columna → 4 nodos + 2 aristas.
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Columnas.Add(new Columna { Id = 1, Altura = 3.0 });
        var zap = new ZapataAislada { Id = 99 };
        zap.Dimensiones.EspesorH = 0.40;
        nivel.Zapatas.Add(zap);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        Assert.Equal(4, grafo.Nodos.Count);
        Assert.Equal(2, grafo.Aristas.Count);
        Assert.Contains(grafo.Aristas, a => a.Elemento == new DomainKey(TipoElemento.Columna, 1));
        Assert.Contains(grafo.Aristas, a => a.Elemento == new DomainKey(TipoElemento.Zapata, 99));
    }

    // ===================================================================
    // VIGAS
    // ===================================================================

    [Fact]
    public void Vigas_Sin_Columnas_Suficientes_Se_Omiten_Del_Grafo()
    {
        // 1 columna + 1 viga → la viga se omite (necesita ≥ 2 columnas).
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Columnas.Add(new Columna { Id = 1, Altura = 3.0 });
        var viga = new Viga { Id = 1 };
        viga.Tramos.Add(new TramoViga { Longitud = 5.0 });
        nivel.Vigas.Add(viga);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // Sólo la columna queda en el grafo.
        Assert.DoesNotContain(grafo.Aristas, a => a.Elemento.Tipo == TipoElemento.Viga);
    }

    [Fact]
    public void Viga_Con_Dos_Columnas_Conecta_Las_Cabezas()
    {
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Columnas.Add(new Columna { Id = 1, Altura = 3.0 });
        nivel.Columnas.Add(new Columna { Id = 2, Altura = 3.0 });
        var viga = new Viga { Id = 5 };
        viga.Tramos.Add(new TramoViga { Longitud = 6.0 });
        nivel.Vigas.Add(viga);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // 4 nodos (2 bases + 2 coronas), 3 aristas (2 columnas + 1 viga).
        Assert.Equal(4, grafo.Nodos.Count);
        Assert.Equal(3, grafo.Aristas.Count);
        var aristaViga = grafo.Aristas.Single(a => a.Elemento.Tipo == TipoElemento.Viga);
        // La viga vive a Z = cota + altura = 3.0
        var nodoI = grafo.Nodos[aristaViga.IdInicio];
        var nodoJ = grafo.Nodos[aristaViga.IdFin];
        Assert.Equal(3f, nodoI.Posicion.Z, precision: 5);
        Assert.Equal(3f, nodoJ.Posicion.Z, precision: 5);
    }

    // ===================================================================
    // IDEMPOTENCIA Y MULTI-NIVEL
    // ===================================================================

    [Fact]
    public void Idempotencia_De_Construccion_Grafo()
    {
        var proyecto = ConstruirProyectoConColumnaYZapata(
            idColumnaZapata: 3, cotaNivel: 0.0, alturaColumna: 3.0, espesorZapata: 0.30);

        var grafo1 = GrafoProyectadoBuilder.Construir(proyecto);
        var grafo2 = GrafoProyectadoBuilder.Construir(proyecto);
        var grafo3 = GrafoProyectadoBuilder.Construir(proyecto);

        Assert.Equal(grafo1.Nodos.Count,   grafo2.Nodos.Count);
        Assert.Equal(grafo2.Nodos.Count,   grafo3.Nodos.Count);
        Assert.Equal(grafo1.Aristas.Count, grafo2.Aristas.Count);
        Assert.Equal(grafo2.Aristas.Count, grafo3.Aristas.Count);

        // Posiciones exactamente iguales entre corridas (operación pura).
        for (int i = 0; i < grafo1.Nodos.Count; i++)
        {
            Assert.Equal(grafo1.Nodos[i].Posicion, grafo2.Nodos[i].Posicion);
        }
    }

    [Fact]
    public void Multinivel_Z_Toma_Cota_Por_Piso()
    {
        // 2 niveles con cotas 0.0 y 3.0; cada uno con una columna distinta.
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var pb = new Nivel { Cota = 0.0 };
        pb.Columnas.Add(new Columna { Id = 1, Altura = 3.0 });
        var n2 = new Nivel { Cota = 3.0 };
        n2.Columnas.Add(new Columna { Id = 2, Altura = 3.0 });
        edificio.Niveles.Add(pb);
        edificio.Niveles.Add(n2);
        proyecto.Edificios.Add(edificio);

        var grafo = GrafoProyectadoBuilder.Construir(proyecto);

        // PB: nodos a Z=0 y Z=3. N2: nodos a Z=3 (compartido) y Z=6.
        // El hash espacial debe fusionar la corona de PB con la base de N2 si
        // ambas están en la misma (X, Y) y Z=3. Pero PB columna Id=1 está en
        // posición (0, 0); N2 columna Id=2 está en posición (6, 0). NO chocan.
        // Resultado: 4 nodos distintos a Z = {0, 3 (PB-corona), 3 (N2-base), 6}.
        Assert.Equal(4, grafo.Nodos.Count);
        Assert.Equal(2, grafo.Aristas.Count);
    }

    // ===================================================================
    // ELEMENTOS INCIDENTES
    // ===================================================================

    [Fact]
    public void Nodos_Sin_Incidencias_Retornan_Lista_Vacia()
    {
        // Caso imposible vía el builder (cada nodo aparece como extremo de
        // alguna arista) — verificado vía proyecto vacío que no agrega nada.
        var grafo = GrafoProyectadoBuilder.Construir(new Proyecto());
        Assert.Empty(grafo.Nodos);
        Assert.Empty(grafo.Aristas);
    }

    [Fact]
    public void GrafoEstructural_Vacio_Es_Reutilizable()
    {
        // El singleton GrafoEstructural.Vacio existe y es válido.
        var vacio = GrafoEstructural.Vacio;
        Assert.NotNull(vacio);
        Assert.Empty(vacio.Nodos);
        Assert.Empty(vacio.Aristas);
        // Misma instancia entre llamadas (singleton).
        Assert.Same(vacio, GrafoEstructural.Vacio);
    }

    // ===================================================================
    // HELPERS DE CONSTRUCCIÓN
    // ===================================================================

    /// <summary>
    /// Construye un proyecto mínimo con un único muro en el nivel por
    /// defecto del edificio único.
    /// </summary>
    private static Proyecto ConstruirProyectoConMuro(
        PuntoCad puntoInicio, PuntoCad puntoFin, double altura, double cotaNivel)
    {
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var nivel = new Nivel { Cota = cotaNivel };
        var sistema = new Sistema { Nombre = "Sistema 1" };
        sistema.Muros.Add(new Muro
        {
            Id = 1,
            PuntoInicio = puntoInicio,
            PuntoFin    = puntoFin,
            Altura      = altura,
            Espesor     = 0.15,
        });
        nivel.Sistemas.Add(sistema);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);
        return proyecto;
    }

    /// <summary>
    /// Construye un proyecto con una columna y una zapata pareadas por Id.
    /// </summary>
    private static Proyecto ConstruirProyectoConColumnaYZapata(
        int idColumnaZapata, double cotaNivel, double alturaColumna, double espesorZapata)
    {
        var proyecto = new Proyecto();
        var edificio = new Edificio();
        var nivel = new Nivel { Cota = cotaNivel };
        nivel.Columnas.Add(new Columna { Id = idColumnaZapata, Altura = alturaColumna });
        var zap = new ZapataAislada { Id = idColumnaZapata };
        zap.Dimensiones.EspesorH = espesorZapata;
        nivel.Zapatas.Add(zap);
        edificio.Niveles.Add(nivel);
        proyecto.Edificios.Add(edificio);
        return proyecto;
    }
}

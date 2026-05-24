// =====================================================================
// SafMapper — Capa pura de mapeo desde el modelo de dominio v3
// (LosasPlus.Models.Proyecto) hacia el modelo de objetos del SDK oficial
// SAF (Structural Analysis Format 2.2.0, SCIA / Nemetschek).
//
// Fase INTEROP-I1 (Parte A) del Plan Maestro de Expansión 3D — EstructurasRD.
//
// Decisiones de diseño:
//   1. EL MODELO SAF ES INMUTABLE: el SDK expone un único constructor
//      `ExcelModel(IReadOnlyCollection<IExcelModuleObject>, …)`. Por lo
//      tanto el mapper construye una lista plana de `IExcelModuleObject`
//      con todas las entidades del proyecto y la pasa íntegra al ctor.
//
//   2. ENTIDAD POR CADA TIPO DE DOMINIO:
//      Viga / Columna  → ExcelStructuralCurveMember (Type=Beam/Column)
//      Muro            → ExcelStructuralSurfaceMember (Type=Wall)
//      Zapata          → ExcelStructuralPointSupport (rígido 6 GDL)
//      Nodos sintéticos → ExcelStructuralPointConnection
//
//   3. CONSOLIDACIÓN DE NODOS: tolerancia 1 mm vía HashEspacialUniforme
//      (default ETABS / SAP2000). Reutilizamos el `GrafoProyectadoBuilder`
//      del namespace LosasPlus.Topologia para garantizar coherencia con
//      el visor 3D y con la futura auditoría cruzada.
//
//   4. SECCIONES Y MATERIAL: catálogos de-duplicados por (b, h) para
//      producir un único ExcelStructuralCrossSection por geometría y un
//      único ExcelStructuralMaterial (Concreto C30/37 por defecto) por
//      proyecto. Reduce drásticamente el tamaño del .xlsx.
//
//   5. UNIDADES: el SDK exige `UnitsNet.Length` y `UnitsNet.Angle` en
//      ciertas propiedades (X/Y/Z, Thickness, Rotation). Se usan los
//      constructores `Length.FromMeters` y `Angle.FromDegrees` para
//      mantener todo el dominio LosasPlus en SI base (metros, radianes).
//
//   6. NAMESPACE COLLISION: el RootNamespace de este proyecto es
//      `LosasPlus.Interop.SAF`. El segmento `SAF` colisiona con el
//      namespace raíz del SDK (`SAF.DataAccess…`). Por lo tanto se usa
//      el prefijo `global::SAF.…` en TODAS las referencias al SDK para
//      forzar la resolución desde la raíz global y evitar errores CS0234.
//
//   7. AISLAMIENTO: este proyecto referencia `src.Core` (modelo de
//      dominio) y el paquete SDK. NO referencia HelixToolkit, WPF ni
//      OxyPlot — mantiene la regla arquitectónica de la suite.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using LosasPlus.Models;
using LosasPlus.Topologia;

// Alias globales para escapar la colisión con LosasPlus.Interop.SAF.* :
// dentro de este namespace, `SAF.…` se resuelve a un sub-namespace
// inexistente del proyecto; `global::SAF.…` salta a la raíz del CLR
// donde sí vive el SDK.
using SafModels      = global::SAF.DataAccess.Models;
using SafElements    = global::SAF.DataAccess.Models.StructuralElements;
using SafLibraries   = global::SAF.DataAccess.Models.Libraries;
using SafEnums       = global::SAF.DataAccess.Models.Enums;
using SafInterfaces  = global::SAF.DataAccess.Models.Interfaces;
using SafSubtypes    = global::SAF.DataAccess.Models.Subtypes;
using UnitsNet;

namespace LosasPlus.Interop.SAF;

/// <summary>
/// Convierte un <see cref="Proyecto"/> v3 de LosasPlus / EstructurasRD
/// en un <see cref="SafModels.ExcelModel"/> consumible por el SDK oficial
/// <c>StructuralAnalysisFormat 1.7.3</c> (SAF 2.2.0).
///
/// <para>
/// El mapper es <b>puro</b>: no muta el proyecto de entrada y no realiza
/// I/O. La escritura a disco la asume <c>SafExporter</c> (Parte B).
/// </para>
///
/// <para>
/// Reglas de mapeo:
/// <list type="bullet">
///   <item><c>NodoSintetico</c> → <c>ExcelStructuralPointConnection</c>
///   con nombre "N{Id+1}".</item>
///   <item><c>AristaSintetica{Viga}</c>   → <c>ExcelStructuralCurveMember</c>
///   <c>(Type = Beam)</c>.</item>
///   <item><c>AristaSintetica{Columna}</c> → <c>ExcelStructuralCurveMember</c>
///   <c>(Type = Column)</c>.</item>
///   <item><c>Muro</c> → <c>ExcelStructuralSurfaceMember</c> <c>(Type =
///   Wall)</c> con un rectángulo de 4 vértices.</item>
///   <item><c>Zapata</c> → <c>ExcelStructuralPointSupport</c> con
///   <c>Fixed</c> en los 6 GDL.</item>
/// </list>
/// </para>
/// </summary>
public static class SafMapper
{
    /// <summary>Nombre por defecto del material de concreto del proyecto.</summary>
    public const string MaterialConcretoPorDefecto = "C30/37";

    /// <summary>Tolerancia de fusión de nodos en milímetros (default CSI).</summary>
    public const double ToleranciaFusionNodosMm = 1.0;

    /// <summary>
    /// Mapea el proyecto completo al modelo SAF. Devuelve un
    /// <see cref="SafModels.ExcelModel"/> listo para ser exportado por
    /// <c>IExcelExportService</c> en la versión SAF deseada.
    /// </summary>
    /// <param name="proyecto">Proyecto v3 a exportar.</param>
    /// <returns>Modelo SAF poblado con todas las entidades convertibles.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="proyecto"/> es null.</exception>
    public static SafModels.ExcelModel MapearProyecto(Proyecto proyecto)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));

        // Acumulador único: todas las entidades del modelo SAF viajan en
        // una lista plana de IExcelModuleObject (rechazo del polimorfismo
        // por propiedad de colección — el SDK 1.7.3 no las expone como
        // setters públicos).
        var objetos = new List<SafInterfaces.IExcelModuleObject>();

        // Contadores locales — re-iniciados en cada llamada para mantener
        // la pureza de la función (sin estado global).
        int contadorVigas      = 0;
        int contadorColumnas   = 0;
        int contadorSuperficies = 0;
        int contadorApoyos     = 0;

        // 1) Construir el grafo proyectado: garantiza fusión de nodos a 1 mm.
        var grafo = GrafoProyectadoBuilder.Construir(proyecto, ToleranciaFusionNodosMm);

        // 2) Catálogo de materiales — un único concreto para el proyecto.
        //    SAF 2.2.0 exige al menos un material para que las secciones sean válidas.
        objetos.Add(ConstruirMaterialConcretoPorDefecto());

        // 3) Catálogo de secciones — de-duplicado por (b, h).
        var catalogoSecciones = ConstruirCatalogoSecciones(proyecto);
        foreach (var seccion in catalogoSecciones.Values)
            objetos.Add(seccion);

        // 4) Nodos sintéticos: emitidos como ExcelStructuralPointConnection.
        foreach (var nodo in grafo.Nodos)
            objetos.Add(ConstruirPointConnection(nodo));

        // 5) Aristas sintéticas: miembros lineales (vigas y columnas).
        //    Aristas de Muro/Zapata se IGNORAN aquí (esos elementos se mapean
        //    a entidades de superficie / apoyo en los pasos 6 y 7).
        foreach (var arista in grafo.Aristas)
        {
            switch (arista.Elemento.Tipo)
            {
                case TipoElemento.Viga:
                    objetos.Add(ConstruirCurveMember(
                        arista, proyecto, esViga: true,
                        catalogoSecciones, ref contadorVigas, ref contadorColumnas));
                    break;
                case TipoElemento.Columna:
                    objetos.Add(ConstruirCurveMember(
                        arista, proyecto, esViga: false,
                        catalogoSecciones, ref contadorVigas, ref contadorColumnas));
                    break;
                default:
                    break;
            }
        }

        // 6) Muros: ExcelStructuralSurfaceMember rectangular (Type=Wall).
        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                foreach (var sistema in nivel.Sistemas)
                {
                    foreach (var muro in sistema.Muros)
                    {
                        var surface = TryConstruirSurfaceMember(
                            muro, nivel.Cota, grafo, ref contadorSuperficies);
                        if (surface is not null) objetos.Add(surface);
                    }
                }
            }
        }

        // 7) Zapatas: ExcelStructuralPointSupport empotrado en el nodo base.
        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                foreach (var zapata in nivel.Zapatas)
                {
                    var apoyo = TryConstruirPointSupport(
                        zapata, nivel.Cota, grafo, ref contadorApoyos);
                    if (apoyo is not null) objetos.Add(apoyo);
                }
            }
        }

        // Argumentos posicionales: el SDK 1.7.3 no expone los nombres
        // formales de los parámetros del constructor de ExcelModel, así
        // que llamamos sin nombres (orden: objects, validations, units).
        return new SafModels.ExcelModel(
            objetos,
            Array.Empty<SafModels.ExcelValidationResult>(),
            SafEnums.ExcelSystemOfUnits.Metric);
    }

    // =================================================================
    // MATERIAL
    // =================================================================

    /// <summary>
    /// Construye el material default de concreto C30/37 con TODAS las
    /// constantes mecánicas exigidas por el validador FluentValidation
    /// del SDK SAF (Patch Deuda I1 — Fase INTEROP-I1).
    ///
    /// <para>
    /// Antes del patch este método sólo poblaba <c>Name</c> y <c>Type</c>;
    /// el validador del SDK rechazaba el material por inconsistencia y, en
    /// cascada, descartaba TODAS las hojas baseline del .xlsx (secciones y
    /// miembros quedaban huérfanos). Resultado: el archivo sólo contenía
    /// la hoja custom EstructurasRD_Design — SCIA Engineer / RFEM no veían
    /// nada al abrirlo.
    /// </para>
    ///
    /// <para>
    /// Valores nominales adoptados (Eurocódigo 2 §3.1.3 / ACI 318-19 §19.2.2):
    /// <list type="bullet">
    ///   <item>E = 33 GPa — módulo de elasticidad del concreto C30/37
    ///   (ACI: Ec ≈ 4700·√f'c con f'c = 30 MPa).</item>
    ///   <item>ν = 0.2 — coeficiente de Poisson del concreto no agrietado
    ///   (EC2 §3.1.3(4)).</item>
    ///   <item>ρ = 2500 kg/m³ — densidad del concreto armado estándar.</item>
    ///   <item>G = E / (2(1+ν)) = 13.75 GPa — derivado por la relación
    ///   elástica clásica de sólido isótropo.</item>
    /// </list>
    /// </para>
    /// </summary>
    private static SafLibraries.ExcelStructuralMaterial ConstruirMaterialConcretoPorDefecto()
    {
        // Constantes nominales (no varían con el proyecto — el material
        // por defecto representa siempre el mismo C30/37 estándar).
        const double EmGPa            = 33.0;       // Módulo de elasticidad
        const double PoissonNu        = 0.2;        // Coeficiente de Poisson
        const double DensityKgPerM3   = 2500.0;     // Densidad másica
        const double ThermalAlphaPerC = 10e-6;      // Coef. expansión térmica (EC2)
        // G = E / (2(1+ν)) — relación clásica para sólido elástico isótropo.
        const double GmGPa            = EmGPa / (2.0 * (1.0 + PoissonNu));

        return new SafLibraries.ExcelStructuralMaterial
        {
            Name               = MaterialConcretoPorDefecto,
            Quality            = MaterialConcretoPorDefecto,   // calidad EN/ACI
            Type               = SafEnums.ExcelMaterialType.Concrete,
            EModulus           = Pressure.FromGigapascals(EmGPa),
            GModulus           = Pressure.FromGigapascals(GmGPa),
            PoissonCoefficient = PoissonNu,
            UnitMass           = Density.FromKilogramsPerCubicMeter(DensityKgPerM3),
            ThermalExpansion   = CoefficientOfThermalExpansion.FromInverseDegreeCelsius(
                                     ThermalAlphaPerC),
        };
    }

    // =================================================================
    // SECCIONES — catálogo de-duplicado por (b, h)
    // =================================================================

    /// <summary>
    /// Recorre las secciones de todas las vigas (sus tramos) y de todas las
    /// columnas del proyecto, y construye un diccionario con un único
    /// <see cref="SafLibraries.ExcelStructuralCrossSection"/> por
    /// dimensión (b × h) rectangular.
    /// </summary>
    private static Dictionary<string, SafLibraries.ExcelStructuralCrossSection>
        ConstruirCatalogoSecciones(Proyecto proyecto)
    {
        var catalogo = new Dictionary<string, SafLibraries.ExcelStructuralCrossSection>(
            StringComparer.Ordinal);

        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                foreach (var viga in nivel.Vigas)
                {
                    foreach (var tramo in viga.Tramos)
                    {
                        if (tramo.Seccion is null) continue;
                        double bMm = tramo.Seccion.Base    * 1000.0;
                        double hMm = tramo.Seccion.Peralte * 1000.0;
                        RegistrarSeccion(catalogo, bMm, hMm);
                    }
                }
                foreach (var columna in nivel.Columnas)
                {
                    double bMm = columna.Geometria.Base    * 1000.0;
                    double hMm = columna.Geometria.Peralte * 1000.0;
                    RegistrarSeccion(catalogo, bMm, hMm);
                }
            }
        }

        // Fallback: si no hay secciones (proyecto vacío) se garantiza al
        // menos una entrada para que los miembros referencien algo válido.
        if (catalogo.Count == 0)
            RegistrarSeccion(catalogo, 300.0, 500.0);

        return catalogo;
    }

    private static void RegistrarSeccion(
        Dictionary<string, SafLibraries.ExcelStructuralCrossSection> catalogo,
        double anchoMm, double peralteMm)
    {
        string nombre = NombreSeccion(anchoMm, peralteMm);
        if (catalogo.ContainsKey(nombre)) return;

        // FormCode es int? en el SDK (acepta valores propios + estándar);
        // se castea explícitamente desde la enum. Parameters es Length[]
        // — para sección rectangular SAF 2.2.0: Parameters[0]=B, [1]=H.
        // Shape es REQUERIDO por el validador FluentValidation (sin él
        // descarta la sección y por cascada la baseline entera) —
        // ExcelProfileLibraryId.Rectangle identifica el perfil rectangular
        // sólido estándar.
        var seccion = new SafLibraries.ExcelStructuralCrossSection
        {
            Name             = nombre,
            Material         = MaterialConcretoPorDefecto,
            CrossSectionType = SafEnums.ExcelCrossSectionType.Parametric,
            Shape            = SafEnums.ExcelProfileLibraryId.Rectangle,
            FormCode         = (int?)SafEnums.ExcelFormCode.FullRectangularSection,
            Parameters       = new[]
            {
                Length.FromMillimeters(anchoMm),
                Length.FromMillimeters(peralteMm),
            },
        };
        catalogo[nombre] = seccion;
    }

    private static string NombreSeccion(double anchoMm, double peralteMm)
        => FormattableString.Invariant($"R_{anchoMm:0}x{peralteMm:0}");

    // =================================================================
    // NODOS
    // =================================================================

    private static SafElements.ExcelStructuralPointConnection ConstruirPointConnection(
        NodoSintetico nodo)
        => new SafElements.ExcelStructuralPointConnection
        {
            Name = NombreNodo(nodo.Id),
            X    = Length.FromMeters(nodo.Posicion.X),
            Y    = Length.FromMeters(nodo.Posicion.Y),
            Z    = Length.FromMeters(nodo.Posicion.Z),
        };

    private static string NombreNodo(int idNodo)
        => FormattableString.Invariant($"N{idNodo + 1}");

    // =================================================================
    // CURVE MEMBERS (Vigas y Columnas)
    // =================================================================

    private static SafElements.ExcelStructuralCurveMember ConstruirCurveMember(
        AristaSintetica arista, Proyecto proyecto, bool esViga,
        IReadOnlyDictionary<string, SafLibraries.ExcelStructuralCrossSection> catalogoSecciones,
        ref int contadorVigas, ref int contadorColumnas)
    {
        string nombreMiembro = esViga
            ? FormattableString.Invariant($"B{++contadorVigas}")
            : FormattableString.Invariant($"C{++contadorColumnas}");

        string nombreSeccion = ResolverNombreSeccion(arista.Elemento, proyecto, catalogoSecciones);
        string nodoIni = NombreNodo(arista.IdInicio);
        string nodoFin = NombreNodo(arista.IdFin);

        // NodeStartName/NodeEndName son read-only en el SDK: se computan
        // desde la colección Nodes. Por eso pasamos los nombres por Nodes.
        // Type es ExcelFlexibleEnum<ExcelMember1DType> — wrapper que admite
        // valores fuera de la enum (campo "otro" para extensibilidad SAF).
        //
        // Segments y LCSAdjustmentLCS son REQUERIDOS por el validador:
        //   - Segments: lista de ExcelCurveShape — un miembro recto entre
        //     dos nodos consecutivos requiere un solo segmento Line.
        //   - LCSAdjustmentLCS: tipo de sistema local — Standard usa la
        //     convención por defecto del SDK (equivalente a nuestra CSI
        //     ya validada en Parte A).
        var tipo1D = esViga ? SafEnums.ExcelMember1DType.Beam
                            : SafEnums.ExcelMember1DType.Column;
        return new SafElements.ExcelStructuralCurveMember
        {
            Name                  = nombreMiembro,
            Type                  = new SafSubtypes.ExcelFlexibleEnum<SafEnums.ExcelMember1DType>(tipo1D),
            CrossSection          = nombreSeccion,
            Nodes                 = new[] { nodoIni, nodoFin },
            Segments              = new[]
            {
                new SafSubtypes.ExcelCurveShape(SafEnums.ExcelCurveGeometricalShape.Line),
            },
            // VectorY: el sistema local se orienta vía un vector hacia +Y.
            // (Standard fue marcada obsoleta por SCIA en 1.7.x — "Is the
            // same as VectorY". Usamos el nombre nuevo.) El validador
            // SAF exige adicionalmente las coordenadas LCS no nulas;
            // cero significa "sin offset" — el sistema local cae sobre
            // el eje longitudinal del miembro tal cual.
            LCSAdjustmentLCS      = SafEnums.ExcelCurveLCSType.VectorY,
            LCSAdjustmentX        = Length.Zero,
            LCSAdjustmentY        = Length.Zero,
            LCSAdjustmentZ        = Length.Zero,
            LCSAdjustmentRotation = Angle.FromDegrees(0.0),
        };
    }

    /// <summary>
    /// Encuentra el nombre de la sección rectangular del elemento de dominio
    /// referenciado por la arista (Viga: usa el primer tramo; Columna: usa
    /// las dimensiones declaradas). Si no se halla, devuelve el primer
    /// nombre del catálogo (fallback determinista).
    /// </summary>
    private static string ResolverNombreSeccion(
        DomainKey elemento, Proyecto proyecto,
        IReadOnlyDictionary<string, SafLibraries.ExcelStructuralCrossSection> catalogo)
    {
        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                if (elemento.Tipo == TipoElemento.Viga)
                {
                    foreach (var viga in nivel.Vigas)
                    {
                        if (viga.Id != elemento.Id) continue;
                        if (viga.Tramos.Count == 0 || viga.Tramos[0].Seccion is null)
                            return PrimerNombreCatalogo(catalogo);
                        var sec = viga.Tramos[0].Seccion!;
                        return NombreSeccion(sec.Base * 1000.0, sec.Peralte * 1000.0);
                    }
                }
                else if (elemento.Tipo == TipoElemento.Columna)
                {
                    foreach (var col in nivel.Columnas)
                    {
                        if (col.Id != elemento.Id) continue;
                        return NombreSeccion(col.Geometria.Base    * 1000.0,
                                             col.Geometria.Peralte * 1000.0);
                    }
                }
            }
        }
        return PrimerNombreCatalogo(catalogo);
    }

    private static string PrimerNombreCatalogo(
        IReadOnlyDictionary<string, SafLibraries.ExcelStructuralCrossSection> catalogo)
    {
        foreach (var key in catalogo.Keys) return key;
        return NombreSeccion(300.0, 500.0);
    }

    // =================================================================
    // MUROS — ExcelStructuralSurfaceMember (Wall) con un rectángulo
    // =================================================================

    private static SafElements.ExcelStructuralSurfaceMember? TryConstruirSurfaceMember(
        LosasPlus.Models.Cad.Muro muro,
        double zNivel,
        IGrafoEstructural grafo,
        ref int contadorSuperficies)
    {
        double altura = muro.Altura > 0.0 ? muro.Altura : 3.0;

        // Localizar los 4 nodos del rectángulo del muro en el grafo
        // (insertados por GrafoProyectadoBuilder con tolerancia 1 mm).
        var p0 = new Vector3((float)muro.PuntoInicio.X, (float)muro.PuntoInicio.Y, (float)zNivel);
        var p1 = new Vector3((float)muro.PuntoFin.X,    (float)muro.PuntoFin.Y,    (float)zNivel);
        var p2 = new Vector3((float)muro.PuntoFin.X,    (float)muro.PuntoFin.Y,    (float)(zNivel + altura));
        var p3 = new Vector3((float)muro.PuntoInicio.X, (float)muro.PuntoInicio.Y, (float)(zNivel + altura));

        int id0 = LocalizarNodoEnGrafo(grafo, p0);
        int id1 = LocalizarNodoEnGrafo(grafo, p1);
        int id2 = LocalizarNodoEnGrafo(grafo, p2);
        int id3 = LocalizarNodoEnGrafo(grafo, p3);

        if (id0 < 0 || id1 < 0 || id2 < 0 || id3 < 0) return null;   // muro huérfano

        double espesorM = muro.Espesor > 0.0 ? muro.Espesor : 0.20;

        // Type es ExcelFlexibleEnum<ExcelMember2DType> — wrapper extensible.
        return new SafElements.ExcelStructuralSurfaceMember
        {
            Name      = FormattableString.Invariant($"W{++contadorSuperficies}"),
            Type      = new SafSubtypes.ExcelFlexibleEnum<SafEnums.ExcelMember2DType>(
                            SafEnums.ExcelMember2DType.Wall),
            Material  = MaterialConcretoPorDefecto,
            Thickness = ConstruirEspesorConstante(espesorM),
            // Polígono perimetral del muro: 4 nodos en orden CCW visto desde +Y.
            Nodes     = new[]
            {
                NombreNodo(id0),
                NombreNodo(id1),
                NombreNodo(id2),
                NombreNodo(id3),
            },
        };
    }

    /// <summary>
    /// Construye un espesor SAF de distribución constante a partir de un
    /// valor en metros. El SDK 1.7.3 modela el espesor como un objeto con
    /// propiedades — no acepta un escalar — para soportar futuras
    /// distribuciones variables.
    /// </summary>
    private static SafSubtypes.ExcelMemberThickness ConstruirEspesorConstante(double espesorM)
        => new SafSubtypes.ExcelMemberThickness
        {
            // Distribución de tipo "Constant" — único Length aplicado en
            // toda la superficie. Las propiedades ThicknessSecond/Third y
            // PointFirst/Second/Third quedan en null (sólo aplican para
            // distribuciones variables en 1 o 2 direcciones).
            Distribution    = SafEnums.ExcelSurfaceDistributionVarying.Constant,
            ThicknessFirst  = Length.FromMeters(espesorM),
        };

    /// <summary>
    /// Busca el nodo del grafo más cercano a la posición dada dentro de la
    /// tolerancia de fusión. Devuelve <c>-1</c> si no hay coincidencia
    /// (caso defensivo — no debería ocurrir si el grafo lo construyó el
    /// mismo builder con la misma tolerancia).
    /// </summary>
    private static int LocalizarNodoEnGrafo(IGrafoEstructural grafo, Vector3 posicion)
    {
        float toleranciaM2 = (float)(ToleranciaFusionNodosMm * 1e-3);
        toleranciaM2 *= toleranciaM2;   // distancia al cuadrado

        for (int i = 0; i < grafo.Nodos.Count; i++)
        {
            var n = grafo.Nodos[i];
            if ((n.Posicion - posicion).LengthSquared() <= toleranciaM2)
                return n.Id;
        }
        return -1;
    }

    // =================================================================
    // ZAPATAS — ExcelStructuralPointSupport rígido (Fixed) en el nodo base
    // =================================================================

    private static SafElements.ExcelStructuralPointSupport? TryConstruirPointSupport(
        LosasPlus.Zapatas.ZapataAislada zapata,
        double zNivel,
        IGrafoEstructural grafo,
        ref int contadorApoyos)
    {
        // El builder asignó la zapata a la posición de grilla artificial
        // determinista por Id (paso 6 m, 8 columnas/fila). Replicamos esa
        // posición para localizar el nodo base de la zapata.
        var (x, y) = GrafoProyectadoBuilder.PosicionEnGrillaArtificial(
            zapata.Id > 0 ? zapata.Id : 1);
        double espesor = zapata.Dimensiones.EspesorH > 0
            ? zapata.Dimensiones.EspesorH
            : 0.30;
        var pBase = new Vector3((float)x, (float)y, (float)(zNivel - espesor));

        int idNodoBase = LocalizarNodoEnGrafo(grafo, pBase);
        if (idNodoBase < 0) return null;

        return new SafElements.ExcelStructuralPointSupport
        {
            Name              = FormattableString.Invariant($"S{++contadorApoyos}"),
            // `Type` define qué tipo de constraint (Fixed/Hinged/Sliding/Custom).
            Type              = SafEnums.ExcelBoundaryNodeCondition.Fixed,
            // `BoundaryCondition` define dónde se aplica (InNode/OnBeam).
            BoundaryCondition = SafEnums.ExcelStructuralPointSupportType.InNode,
            Node              = NombreNodo(idNodoBase),
            CoordinateSystem  = SafEnums.ExcelCoordinateSystem.Global,
            // Empotramiento completo: rígido en los 6 GDL.
            TranslationXType  = SafEnums.ExcelConstraintType.Rigid,
            TranslationYType  = SafEnums.ExcelConstraintType.Rigid,
            TranslationZType  = SafEnums.ExcelConstraintType.Rigid,
            RotationXType     = SafEnums.ExcelConstraintType.Rigid,
            RotationYType     = SafEnums.ExcelConstraintType.Rigid,
            RotationZType     = SafEnums.ExcelConstraintType.Rigid,
        };
    }
}

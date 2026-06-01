using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using LosasPlus.Cargas;
using LosasPlus.Vigas;

namespace LosasPlus.Services;

/// <summary>
/// Exporta el modelo de vigas continuas del proyecto al
/// <b>SAF (Structural Analysis Format)</b> — el formato abierto basado en Excel
/// de IDEA StatiCa para intercambiar modelos de análisis estructural entre
/// programas (SCIA Engineer, IDEA StatiCa, RFEM, etc.).
///
/// <para>
/// Un libro SAF tiene <b>una hoja por tipo de objeto</b>, con la fila 1 como
/// cabecera de columnas. Este exportador escribe las tablas necesarias para un
/// modelo 1D de vigas:
/// <list type="bullet">
///   <item><c>StructuralMaterial</c> — un material por cada E distinto.</item>
///   <item><c>StructuralCrossSection</c> — una sección por cada (b,h,material) distinto.</item>
///   <item><c>StructuralPointConnection</c> — los nodos (fronteras de tramo + apoyos), deduplicados.</item>
///   <item><c>StructuralCurveMember</c> — un miembro 1D por tramo.</item>
///   <item><c>StructuralPointSupport</c> — los apoyos mapeados a GDL.</item>
///   <item><c>StructuralLoadGroup</c> / <c>StructuralLoadCase</c> — la base de casos del proyecto.</item>
///   <item><c>StructuralLoadCombination</c> — las combinaciones (ULS/SLS) con sus factores.</item>
///   <item><c>StructuralCurveMemberLoad</c> — cargas distribuidas sobre tramos.</item>
///   <item><c>StructuralPointLoadOnBeam</c> — cargas puntuales sobre tramos.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Convenios de modelado.</b> El eje de la viga corre sobre el global X; los
/// nodos están en (x, 0, 0). La gravedad es −Z, así que las cargas descendentes
/// (magnitud positiva del dominio) se exportan con signo negativo en Z. El
/// mapeo de apoyos es: <c>Fijo</c> → Uz rígido; <c>Empotrado</c> → Uz+Ry
/// rígidos; <c>Elastico</c> → Uz flexible (rigidez del resorte). En cada apoyo
/// se fijan además los GDL fuera de plano (Uy, Rx, Rz) para que el modelo
/// planar no quede como mecanismo 3D, y Ux se fija sólo en el primer apoyo.
/// </para>
///
/// <para>
/// <b>Unidades.</b> Longitudes en m; fuerzas en kN, momentos en kN·m, módulos
/// en kN/m² — coherente con el modelo de dominio (base SI kN-m). La hoja
/// <c>ProjectInfo</c> declara las unidades explícitamente.
/// </para>
/// </summary>
public static class SafExporter
{
    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x29, 0x37);
    private static readonly XLColor HeaderFont = XLColor.White;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Tolerancia (m) para considerar dos abscisas el mismo nodo.</summary>
    private const double TolNodo = 1e-6;

    /// <summary>
    /// Exporta las <paramref name="vigas"/> y la base de <paramref name="cargas"/>
    /// del proyecto a un libro SAF en <paramref name="safPath"/>.
    /// </summary>
    public static void Export(
        IReadOnlyList<Viga> vigas,
        CombinacionesProyecto cargas,
        string safPath,
        string? proyectoNombre = null)
    {
        if (vigas is null) throw new ArgumentNullException(nameof(vigas));
        if (cargas is null) throw new ArgumentNullException(nameof(cargas));

        // --- Catálogos normalizados (deduplicados por valor) ---
        var materiales = new MaterialCatalogo();
        var secciones = new SeccionCatalogo();

        // Filas acumuladas de cada tabla SAF.
        var nodos = new List<NodoSaf>();
        var miembros = new List<MiembroSaf>();
        var apoyos = new List<ApoyoSaf>();
        var cargasMiembro = new List<CargaMiembroSaf>();
        var cargasPuntuales = new List<CargaPuntualSaf>();

        for (int vi = 0; vi < vigas.Count; vi++)
        {
            var viga = vigas[vi];
            string prefijo = $"V{vi + 1}";

            // Fronteras de tramo: 0, L1, L1+L2, … (abscisas de los nodos del eje).
            var fronteras = new List<double> { 0.0 };
            double acum = 0.0;
            foreach (var tramo in viga.Tramos)
            {
                acum += tramo.Longitud;
                fronteras.Add(acum);
            }

            // Nodos del eje + nodos de apoyo (pueden caer entre fronteras),
            // deduplicados con tolerancia y nombrados Vk_Nj.
            var nodosViga = new NodoIndex(prefijo);
            foreach (var x in fronteras) nodosViga.Obtener(x, nodos);
            foreach (var ap in viga.Apoyos) nodosViga.Obtener(ap.CoordenadaX, nodos);

            // Un StructuralCurveMember por tramo, entre sus nodos frontera.
            for (int t = 0; t < viga.Tramos.Count; t++)
            {
                var tramo = viga.Tramos[t];
                string mat = materiales.Obtener(tramo.ModuloElasticidad);
                string sec = secciones.Obtener(tramo.Base, tramo.Peralte, mat);
                string nIni = nodosViga.Obtener(fronteras[t], nodos);
                string nFin = nodosViga.Obtener(fronteras[t + 1], nodos);
                string nombreMiembro = $"{prefijo}_M{t + 1}";
                miembros.Add(new MiembroSaf(nombreMiembro, sec, nIni, nFin));

                // Cargas del tramo → tablas de carga SAF.
                foreach (var c in tramo.Cargas)
                {
                    if (c.Tipo == TipoCargaElemento.Distribuida)
                        cargasMiembro.Add(new CargaMiembroSaf(
                            $"CD{cargasMiembro.Count + 1}", nombreMiembro,
                            CasoNombre(c.CodigoCaso), -c.Magnitud));
                    else
                        cargasPuntuales.Add(new CargaPuntualSaf(
                            $"CP{cargasPuntuales.Count + 1}", nombreMiembro,
                            CasoNombre(c.CodigoCaso), -c.Magnitud, c.Posicion));
                }
            }

            // Apoyos → StructuralPointSupport (Ux fijo sólo en el primer apoyo).
            for (int a = 0; a < viga.Apoyos.Count; a++)
            {
                var ap = viga.Apoyos[a];
                string nodo = nodosViga.Obtener(ap.CoordenadaX, nodos);
                apoyos.Add(ApoyoSaf.Desde(ap, $"{prefijo}_S{a + 1}", nodo, fijarUx: a == 0));
            }
        }

        using var wb = new XLWorkbook();
        EscribirProjectInfo(wb, proyectoNombre, vigas.Count, nodos.Count, miembros.Count);
        EscribirMateriales(wb, materiales);
        EscribirSecciones(wb, secciones);
        EscribirNodos(wb, nodos);
        EscribirMiembros(wb, miembros);
        EscribirApoyos(wb, apoyos);
        EscribirCasos(wb, cargas);
        EscribirCombinaciones(wb, cargas);
        EscribirCargasMiembro(wb, cargasMiembro);
        EscribirCargasPuntuales(wb, cargasPuntuales);
        wb.SaveAs(safPath);
    }

    // ===== Nombre legible de un caso desde su código =====
    private static string CasoNombre(string codigoCaso)
        => string.IsNullOrWhiteSpace(codigoCaso) ? "LC1" : codigoCaso;

    // ===================== Catálogos =====================

    private sealed class MaterialCatalogo
    {
        private readonly Dictionary<long, string> _porE = new();
        public List<(string Nombre, double E)> Items { get; } = new();

        public string Obtener(double e)
        {
            long clave = (long)Math.Round(e);            // E en kN/m², clave entera estable
            if (_porE.TryGetValue(clave, out var n)) return n;
            string nombre = $"C{Items.Count + 1}";
            _porE[clave] = nombre;
            Items.Add((nombre, e));
            return nombre;
        }
    }

    private sealed class SeccionCatalogo
    {
        private readonly Dictionary<string, string> _porClave = new();
        public List<(string Nombre, double B, double H, string Material)> Items { get; } = new();

        public string Obtener(double b, double h, string material)
        {
            string clave = $"{Math.Round(b, 6)}|{Math.Round(h, 6)}|{material}";
            if (_porClave.TryGetValue(clave, out var n)) return n;
            int mm_b = (int)Math.Round(b * 1000), mm_h = (int)Math.Round(h * 1000);
            string nombre = $"R{mm_b}x{mm_h}";
            // Desambigua si el mismo (b,h) tiene materiales distintos.
            if (Items.Any(s => s.Nombre == nombre)) nombre += $"_{material}";
            _porClave[clave] = nombre;
            Items.Add((nombre, b, h, material));
            return nombre;
        }
    }

    /// <summary>Índice de nodos de una viga: deduplica abscisas y emite nombres Vk_Nj.</summary>
    private sealed class NodoIndex
    {
        private readonly string _prefijo;
        private readonly List<(double X, string Nombre)> _nodos = new();
        public NodoIndex(string prefijo) => _prefijo = prefijo;

        public string Obtener(double x, List<NodoSaf> destino)
        {
            foreach (var (xn, nombre) in _nodos)
                if (Math.Abs(xn - x) <= TolNodo) return nombre;
            string nuevo = $"{_prefijo}_N{_nodos.Count + 1}";
            _nodos.Add((x, nuevo));
            destino.Add(new NodoSaf(nuevo, x));
            return nuevo;
        }
    }

    // ===================== DTOs de fila =====================

    private readonly record struct NodoSaf(string Nombre, double X);
    private readonly record struct MiembroSaf(string Nombre, string Seccion, string NodoIni, string NodoFin);
    private readonly record struct CargaMiembroSaf(string Nombre, string Miembro, string Caso, double Valor);
    private readonly record struct CargaPuntualSaf(string Nombre, string Miembro, string Caso, double Valor, double Pos);

    private readonly record struct ApoyoSaf(
        string Nombre, string Nodo,
        string Ux, string Uy, string Uz, string Rx, string Ry, string Rz)
    {
        public static ApoyoSaf Desde(ApoyoViga ap, string nombre, string nodo, bool fijarUx)
        {
            // GDL fuera de plano siempre rígidos (modelo planar en X-Z, flexión sobre Y).
            string ux = fijarUx ? "Rigid" : "Free";
            string uy = "Rigid", rx = "Rigid", rz = "Rigid";
            string uz, ry;
            switch (ap.Tipo)
            {
                case TipoApoyo.Empotrado:
                    uz = "Rigid"; ry = "Rigid"; break;
                case TipoApoyo.Elastico:
                    uz = ap.RigidezResorte.ToString(Inv); ry = "Free"; break;
                default: // Fijo
                    uz = "Rigid"; ry = "Free"; break;
            }
            return new ApoyoSaf(nombre, nodo, ux, uy, uz, rx, ry, rz);
        }
    }

    // ===================== Escritura de hojas =====================

    private static IXLWorksheet Hoja(XLWorkbook wb, string nombre, params string[] cabeceras)
    {
        var ws = wb.Worksheets.Add(nombre);
        for (int c = 0; c < cabeceras.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cabeceras[c];
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Font.FontColor = HeaderFont;
            cell.Style.Font.Bold = true;
        }
        return ws;
    }

    private static void EscribirProjectInfo(XLWorkbook wb, string? nombre, int nVigas, int nNodos, int nMiembros)
    {
        var ws = Hoja(wb, "ProjectInfo", "Property", "Value");
        var filas = new (string, string)[]
        {
            ("Format", "SAF (Structural Analysis Format)"),
            ("Generator", "EstructurasRD / LosasPlus (Avalonia)"),
            ("Project", nombre ?? "Proyecto"),
            ("Units length", "m"),
            ("Units force", "kN"),
            ("Units moment", "kN*m"),
            ("Beams", nVigas.ToString(Inv)),
            ("Nodes", nNodos.ToString(Inv)),
            ("Members", nMiembros.ToString(Inv)),
        };
        for (int i = 0; i < filas.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = filas[i].Item1;
            ws.Cell(i + 2, 2).Value = filas[i].Item2;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirMateriales(XLWorkbook wb, MaterialCatalogo cat)
    {
        var ws = Hoja(wb, "StructuralMaterial", "Name", "Material type", "E modulus", "Unit");
        int r = 2;
        foreach (var (nombre, e) in cat.Items)
        {
            ws.Cell(r, 1).Value = nombre;
            ws.Cell(r, 2).Value = "Concrete";
            ws.Cell(r, 3).Value = e;
            ws.Cell(r, 4).Value = "kN/m2";
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirSecciones(XLWorkbook wb, SeccionCatalogo cat)
    {
        var ws = Hoja(wb, "StructuralCrossSection",
            "Name", "Material", "Cross-section type", "Width b", "Height h", "Unit");
        int r = 2;
        foreach (var (nombre, b, h, material) in cat.Items)
        {
            ws.Cell(r, 1).Value = nombre;
            ws.Cell(r, 2).Value = material;
            ws.Cell(r, 3).Value = "Rectangle";
            ws.Cell(r, 4).Value = b;
            ws.Cell(r, 5).Value = h;
            ws.Cell(r, 6).Value = "m";
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirNodos(XLWorkbook wb, List<NodoSaf> nodos)
    {
        var ws = Hoja(wb, "StructuralPointConnection", "Name", "X", "Y", "Z");
        int r = 2;
        foreach (var n in nodos)
        {
            ws.Cell(r, 1).Value = n.Nombre;
            ws.Cell(r, 2).Value = n.X;
            ws.Cell(r, 3).Value = 0.0;
            ws.Cell(r, 4).Value = 0.0;
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirMiembros(XLWorkbook wb, List<MiembroSaf> miembros)
    {
        var ws = Hoja(wb, "StructuralCurveMember",
            "Name", "Cross-section", "Begin node", "End node", "Type");
        int r = 2;
        foreach (var m in miembros)
        {
            ws.Cell(r, 1).Value = m.Nombre;
            ws.Cell(r, 2).Value = m.Seccion;
            ws.Cell(r, 3).Value = m.NodoIni;
            ws.Cell(r, 4).Value = m.NodoFin;
            ws.Cell(r, 5).Value = "Beam";
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirApoyos(XLWorkbook wb, List<ApoyoSaf> apoyos)
    {
        var ws = Hoja(wb, "StructuralPointSupport",
            "Name", "Node", "Ux", "Uy", "Uz", "Fix", "Fiy", "Fiz");
        int r = 2;
        foreach (var a in apoyos)
        {
            ws.Cell(r, 1).Value = a.Nombre;
            ws.Cell(r, 2).Value = a.Nodo;
            ws.Cell(r, 3).Value = a.Ux;
            ws.Cell(r, 4).Value = a.Uy;
            ws.Cell(r, 5).Value = a.Uz;
            ws.Cell(r, 6).Value = a.Rx;
            ws.Cell(r, 7).Value = a.Ry;
            ws.Cell(r, 8).Value = a.Rz;
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirCasos(XLWorkbook wb, CombinacionesProyecto cargas)
    {
        var wg = Hoja(wb, "StructuralLoadGroup", "Name", "Load group type");
        wg.Cell(2, 1).Value = "LG_Permanent"; wg.Cell(2, 2).Value = "Permanent";
        wg.Cell(3, 1).Value = "LG_Variable";  wg.Cell(3, 2).Value = "Variable";
        wg.Columns().AdjustToContents();

        var ws = Hoja(wb, "StructuralLoadCase",
            "Name", "Description", "Action type", "Load group");
        int r = 2;
        foreach (var caso in cargas.Casos)
        {
            bool permanente = caso.Tipo == TipoCasoCarga.Muerta;
            ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(caso.Codigo) ? $"LC{r - 1}" : caso.Codigo;
            ws.Cell(r, 2).Value = caso.Nombre;
            ws.Cell(r, 3).Value = permanente ? "Permanent" : "Variable";
            ws.Cell(r, 4).Value = permanente ? "LG_Permanent" : "LG_Variable";
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirCombinaciones(XLWorkbook wb, CombinacionesProyecto cargas)
    {
        var ws = Hoja(wb, "StructuralLoadCombination",
            "Name", "Type", "Load cases", "Coefficients");
        int r = 2;
        foreach (var combo in cargas.Combinaciones)
        {
            var casos = combo.Terminos.Select(t => t.CodigoCaso);
            var factores = combo.Terminos.Select(t => t.Factor.ToString(Inv));
            ws.Cell(r, 1).Value = string.IsNullOrWhiteSpace(combo.Nombre) ? $"CO{r - 1}" : combo.Nombre;
            ws.Cell(r, 2).Value = combo.Tipo == TipoCombinacion.Ultima ? "ULS" : "SLS";
            ws.Cell(r, 3).Value = string.Join(";", casos);
            ws.Cell(r, 4).Value = string.Join(";", factores);
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirCargasMiembro(XLWorkbook wb, List<CargaMiembroSaf> cargas)
    {
        var ws = Hoja(wb, "StructuralCurveMemberLoad",
            "Name", "Member", "Load case", "Direction", "Distribution", "Value 1", "Unit");
        int r = 2;
        foreach (var c in cargas)
        {
            ws.Cell(r, 1).Value = c.Nombre;
            ws.Cell(r, 2).Value = c.Miembro;
            ws.Cell(r, 3).Value = c.Caso;
            ws.Cell(r, 4).Value = "Z";
            ws.Cell(r, 5).Value = "Uniform";
            ws.Cell(r, 6).Value = c.Valor;
            ws.Cell(r, 7).Value = "kN/m";
            r++;
        }
        ws.Columns().AdjustToContents();
    }

    private static void EscribirCargasPuntuales(XLWorkbook wb, List<CargaPuntualSaf> cargas)
    {
        var ws = Hoja(wb, "StructuralPointLoadOnBeam",
            "Name", "Member", "Load case", "Direction", "Value", "Coordinate", "Unit");
        int r = 2;
        foreach (var c in cargas)
        {
            ws.Cell(r, 1).Value = c.Nombre;
            ws.Cell(r, 2).Value = c.Miembro;
            ws.Cell(r, 3).Value = c.Caso;
            ws.Cell(r, 4).Value = "Z";
            ws.Cell(r, 5).Value = c.Valor;
            ws.Cell(r, 6).Value = c.Pos;
            ws.Cell(r, 7).Value = "kN";
            r++;
        }
        ws.Columns().AdjustToContents();
    }
}

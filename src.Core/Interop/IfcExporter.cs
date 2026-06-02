using System.Globalization;
using System.IO;
using System.Text;
using LosasPlus.Models;

namespace LosasPlus.Interop;

/// <summary>
/// Exporta el modelo del edificio al formato abierto <b>IFC 4.3</b> (openBIM,
/// buildingSMART / ISO 16739) en archivo STEP (ISO-10303-21, <c>.ifc</c>) —
/// pieza de interoperabilidad para BIM/GIS y, a futuro, obras de arte y el mapa
/// 3D urbano (CityGML).
///
/// <para>
/// Este primer incremento (Fase K.2) escribe el <b>esqueleto espacial</b>:
/// <c>IfcProject → IfcSite → IfcBuilding → IfcBuildingStorey</c> (uno por nivel,
/// con su cota), unidades SI y contexto geométrico, enlazados con
/// <c>IfcRelAggregates</c>. Los elementos estructurales (IfcColumn/IfcBeam/
/// IfcSlab/IfcFooting) y su geometría llegan en incrementos posteriores.
/// </para>
///
/// <para>
/// Tipo <b>puro de dominio</b> — escribe texto, sin dependencias de UI. La marca
/// de tiempo del HEADER se pasa por parámetro (no se lee del reloj) para que la
/// salida sea determinista y testeable.
/// </para>
/// </summary>
public static class IfcExporter
{
    private const string Alfabeto =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Exporta el <paramref name="edificio"/> a un IFC 4.3 en <paramref name="ifcPath"/>.</summary>
    public static void Export(
        Edificio edificio, string ifcPath, string proyectoNombre = "Proyecto", string fechaIso = "")
    {
        var data = new StringBuilder();
        int id = 0;
        int Emit(string body) { int n = ++id; data.Append('#').Append(n).Append('=').Append(body).Append(";\n"); return n; }

        // Contexto geométrico (mínimo): origen + ejes.
        int pOrigen = Emit("IFCCARTESIANPOINT((0.,0.,0.))");
        int dZ = Emit("IFCDIRECTION((0.,0.,1.))");
        int dX = Emit("IFCDIRECTION((1.,0.,0.))");
        int ejes = Emit($"IFCAXIS2PLACEMENT3D(#{pOrigen},#{dZ},#{dX})");
        int ctx = Emit($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.0E-05,#{ejes},$)");

        // K.6: geometría de un elemento como prisma rectangular vertical —
        // IfcRectangleProfileDef (b×h, centrado) extruido +Z por la altura, vía
        // IfcExtrudedAreaSolid → IfcShapeRepresentation → IfcProductDefinitionShape.
        // El perfil se posiciona en (cx,cy,cz) (IFC: X,Y en planta, Z arriba).
        // Devuelve el id del IfcProductDefinitionShape para usarlo como Representation.
        int PrismaVertical(double cx, double cy, double cz, double b, double h, double profundidad, double dirZComp = 1.0)
        {
            int p2d = Emit("IFCCARTESIANPOINT((0.,0.))");
            int ax2 = Emit($"IFCAXIS2PLACEMENT2D(#{p2d},$)");
            int prof = Emit($"IFCRECTANGLEPROFILEDEF(.AREA.,$,#{ax2},{Real(b)},{Real(h)})");
            int pBase = Emit($"IFCCARTESIANPOINT(({Real(cx)},{Real(cy)},{Real(cz)}))");
            int dirZ = Emit("IFCDIRECTION((0.,0.,1.))");
            int dirX = Emit("IFCDIRECTION((1.,0.,0.))");
            int pos = Emit($"IFCAXIS2PLACEMENT3D(#{pBase},#{dirZ},#{dirX})");
            // Dirección de extrusión: +Z (columna, hacia arriba) o -Z (losa, hacia abajo).
            int ext = Emit($"IFCDIRECTION((0.,0.,{Real(dirZComp)}))");
            int solid = Emit($"IFCEXTRUDEDAREASOLID(#{prof},#{pos},#{ext},{Real(profundidad)})");
            int rep = Emit($"IFCSHAPEREPRESENTATION(#{ctx},'Body','SweptSolid',(#{solid}))");
            return Emit($"IFCPRODUCTDEFINITIONSHAPE($,$,(#{rep}))");
        }

        // Unidades SI (metro).
        int uLong = Emit("IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.)");
        int uAssign = Emit($"IFCUNITASSIGNMENT((#{uLong}))");

        // Jerarquía espacial.
        int proyecto = Emit($"IFCPROJECT('{Guid22(id + 1)}',$,{Txt(proyectoNombre)},$,$,$,$,(#{ctx}),#{uAssign})");
        string refLat = "$", refLon = "$";
        if (edificio is not null && (edificio.Latitud != 0 || edificio.Longitud != 0))
        {
            refLat = AnguloIfc(edificio.Latitud);
            refLon = AnguloIfc(edificio.Longitud);
        }
        int sitio = Emit($"IFCSITE('{Guid22(id + 1)}',$,'Sitio',$,$,$,$,$,.ELEMENT.,{refLat},{refLon},$,$,$)");
        int edif = Emit($"IFCBUILDING('{Guid22(id + 1)}',$,{Txt(edificio?.Nombre ?? "Edificio")},$,$,$,$,$,.ELEMENT.,$,$,$)");

        var pisos = new System.Collections.Generic.List<int>();
        if (edificio is not null)
            foreach (var nivel in edificio.Niveles)
            {
                int piso = Emit(
                    $"IFCBUILDINGSTOREY('{Guid22(id + 1)}',$,{Txt(nivel.Nombre)},$,$,$,$,$,.ELEMENT.,{Real(nivel.Cota)})");
                pisos.Add(piso);

                // Elementos estructurales del nivel.
                var elems = new System.Collections.Generic.List<int>();
                foreach (var columna in nivel.Columnas)
                {
                    int formaCol = PrismaVertical(columna.CoordenadaX, columna.CoordenadaY, nivel.Cota,
                                                  columna.Base, columna.Peralte, columna.Altura);
                    elems.Add(Emit($"IFCCOLUMN('{Guid22(id + 1)}',$,{Txt(columna.Nombre)},$,$,$,#{formaCol},$,$)"));
                    if (columna.Zapata is { } zap)
                    {
                        // Perfil Ancho×Largo centrado en la columna, extruido -Z por el peralte.
                        string repZap = "$";
                        if (zap.Peralte > 0)
                        {
                            int formaZap = PrismaVertical(columna.CoordenadaX, columna.CoordenadaY, nivel.Cota,
                                                          zap.Ancho, zap.Largo, zap.Peralte, -1.0);
                            repZap = "#" + formaZap;
                        }
                        elems.Add(Emit($"IFCFOOTING('{Guid22(id + 1)}',$,{Txt(zap.Nombre)},$,$,$,{repZap},$,$)"));
                    }
                }
                foreach (var viga in nivel.Vigas)
                    elems.Add(Emit($"IFCBEAM('{Guid22(id + 1)}',$,{Txt(viga.Nombre)},$,$,$,$,$,$)"));
                foreach (var sistema in nivel.Sistemas)
                    foreach (var losa in sistema.Losas)
                    {
                        // Perfil Lx×Ly centrado en el centro del paño (esquina + media luz),
                        // extruido hacia abajo (-Z) por el espesor.
                        string repLosa = "$";
                        if (losa.Espesor > 0)
                        {
                            int forma = PrismaVertical(
                                losa.CoordenadaX + losa.Lx / 2.0, losa.CoordenadaY + losa.Ly / 2.0, nivel.Cota,
                                losa.Lx, losa.Ly, losa.Espesor, -1.0);
                            repLosa = "#" + forma;
                        }
                        elems.Add(Emit($"IFCSLAB('{Guid22(id + 1)}',$,{Txt("Losa " + losa.Id)},$,$,$,{repLosa},$,.FLOOR.)"));
                    }

                // Contención de los elementos en el piso.
                if (elems.Count > 0)
                {
                    var er = string.Join(",", elems.ConvertAll(e => "#" + e));
                    Emit($"IFCRELCONTAINEDINSPATIALSTRUCTURE('{Guid22(id + 1)}',$,$,$,({er}),#{piso})");
                }
            }

        // Agregaciones project→site→building→storeys.
        Emit($"IFCRELAGGREGATES('{Guid22(id + 1)}',$,$,$,#{proyecto},(#{sitio}))");
        Emit($"IFCRELAGGREGATES('{Guid22(id + 1)}',$,$,$,#{sitio},(#{edif}))");
        if (pisos.Count > 0)
        {
            var refs = string.Join(",", pisos.ConvertAll(p => "#" + p));
            Emit($"IFCRELAGGREGATES('{Guid22(id + 1)}',$,$,$,#{edif},({refs}))");
        }

        var sb = new StringBuilder();
        sb.Append("ISO-10303-21;\n");
        sb.Append("HEADER;\n");
        sb.Append("FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');\n");
        sb.Append($"FILE_NAME('{Escapar(Path.GetFileName(ifcPath))}','{Escapar(fechaIso)}',(''),(''),'EstructurasRD','EstructurasRD','');\n");
        sb.Append("FILE_SCHEMA(('IFC4X3'));\n");
        sb.Append("ENDSEC;\n");
        sb.Append("DATA;\n");
        sb.Append(data);
        sb.Append("ENDSEC;\n");
        sb.Append("END-ISO-10303-21;\n");

        File.WriteAllText(ifcPath, sb.ToString());
    }

    private static string Txt(string s) => "'" + Escapar(s) + "'";
    private static string Escapar(string s) => (s ?? "").Replace("'", "''");
    private static string Real(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);

    /// <summary>Convierte grados decimales a IfcCompoundPlaneAngleMeasure (grados,minutos,segundos,millonésimas).</summary>
    private static string AnguloIfc(double grados)
    {
        int signo = grados < 0 ? -1 : 1;
        double abs = System.Math.Abs(grados);
        int d = (int)abs;
        double rm = (abs - d) * 60.0;
        int m = (int)rm;
        double rs = (rm - m) * 60.0;
        int s = (int)rs;
        int frac = (int)System.Math.Round((rs - s) * 1_000_000.0);
        if (frac >= 1_000_000) { frac = 0; s++; }
        return $"({signo * d},{signo * m},{signo * s},{signo * frac})";
    }

    /// <summary>GlobalId IFC: 22 chars del alfabeto base64 de IFC, derivado de <paramref name="n"/> (determinista).</summary>
    private static string Guid22(int n)
    {
        var chars = new char[22];
        long v = n;
        for (int i = 21; i >= 0; i--) { chars[i] = Alfabeto[(int)(v % 64)]; v /= 64; }
        return new string(chars);
    }
}

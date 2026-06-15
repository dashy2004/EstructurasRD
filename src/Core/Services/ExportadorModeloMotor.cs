using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LosasPlus.Models;

namespace LosasPlus.Services;

public sealed class ExportadorModeloException : Exception
{
    public ExportadorModeloException(string mensaje) : base(mensaje) { }
}

/// <summary>Ensambla un <see cref="ModeloMotorDto"/> a partir de un <see cref="Edificio"/> (Etapa 1a:
/// geometría del pórtico, sin cargas). Función pura; la escritura a disco vive en ExportadorModeloArchivo.</summary>
public static class ExportadorModeloMotor
{
    public const double NuHormigon = 0.2;
    public const double DensidadHormigon = 2400.0; // kg/m³

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static ModeloMotorDto Exportar(Edificio edificio)
    {
        var (nodos, elementos) = SintetizadorFrame.Sintetizar(edificio);
        if (elementos.Count == 0)
            throw new ExportadorModeloException("El edificio no tiene pórtico (columnas/vigas) que exportar.");

        var modelo = new ModeloMotorDto();
        foreach (var n in nodos)
            modelo.Nodos.Add(new NodoMotor { Id = n.Id, X = n.X, Y = n.Y, Z = n.Z });

        var matIdPorFc = new Dictionary<long, int>();
        int SiguienteMaterial(double fcTonfCm2)
        {
            double fc = fcTonfCm2 > 0 ? fcTonfCm2 : SintetizadorFrame.FcDefecto;
            long clave = (long)Math.Round(fc * 1e6);
            if (matIdPorFc.TryGetValue(clave, out int existente)) return existente;
            int id = modelo.Materiales.Count + 1;
            double fcMPa = fc * MotorFeaConversion.TonfCm2_a_MPa;
            modelo.Materiales.Add(new MaterialMotor
            {
                Id = id,
                E = MotorFeaConversion.ModuloElasticoPa(fcMPa),
                Nu = NuHormigon,
                Densidad = DensidadHormigon,
            });
            matIdPorFc[clave] = id;
            return id;
        }

        var secIdPorBh = new Dictionary<(long, long), int>();
        int SiguienteSeccion(double b, double h)
        {
            var clave = ((long)Math.Round(b * 1000), (long)Math.Round(h * 1000));
            if (secIdPorBh.TryGetValue(clave, out int existente)) return existente;
            int id = modelo.Secciones.Count + 1;
            var p = GeometriaSeccion.Rectangular(b, h);
            modelo.Secciones.Add(new SeccionMotor
            {
                Id = id, Area = p.Area, InerciaY = p.InerciaY,
                InerciaZ = p.InerciaZ, ConstanteTorsion = p.ConstanteTorsion,
            });
            secIdPorBh[clave] = id;
            return id;
        }

        foreach (var e in elementos)
        {
            double b = e.B > 0 ? e.B : SintetizadorFrame.SeccionVigaBaseDefecto;
            double h = e.H > 0 ? e.H : SintetizadorFrame.SeccionVigaPeralteDefecto;
            modelo.Elementos.Add(new ElementoMotor
            {
                Id = e.Id,
                NodoI = e.NodoI,
                NodoJ = e.NodoJ,
                MaterialId = SiguienteMaterial(e.Fc),
                SeccionId = SiguienteSeccion(b, h),
                VectorReferencia = e.EsColumna ? new[] { 1.0, 0.0, 0.0 } : new[] { 0.0, 0.0, 1.0 },
            });
        }

        foreach (int nodoId in NodosApoyo(edificio, nodos))
            modelo.Apoyos.Add(new ApoyoMotor
            {
                NodoId = nodoId, Ux = true, Uy = true, Uz = true, Rx = true, Ry = true, Rz = true,
            });

        // Cargas: vacío en Etapa 1a.
        return modelo;
    }

    public static string ToJson(ModeloMotorDto modelo) => JsonSerializer.Serialize(modelo, JsonOpts);

    /// <summary>Integridad referencial (espejo de core/modelo.py::validar). Vacío = OK.</summary>
    public static List<string> ValidarIntegridad(ModeloMotorDto m)
    {
        var errores = new List<string>();
        var ids = new HashSet<int>();
        foreach (var n in m.Nodos)
            if (!ids.Add(n.Id)) errores.Add($"Nodo duplicado: {n.Id}");
        var mat = m.Materiales.Select(x => x.Id).ToHashSet();
        var sec = m.Secciones.Select(x => x.Id).ToHashSet();
        foreach (var e in m.Elementos)
        {
            if (!ids.Contains(e.NodoI)) errores.Add($"Elemento {e.Id}: nodo_i {e.NodoI} inexistente");
            if (!ids.Contains(e.NodoJ)) errores.Add($"Elemento {e.Id}: nodo_j {e.NodoJ} inexistente");
            if (e.NodoI == e.NodoJ) errores.Add($"Elemento {e.Id}: conecta un nodo consigo mismo");
            if (!mat.Contains(e.MaterialId)) errores.Add($"Elemento {e.Id}: material {e.MaterialId} inexistente");
            if (!sec.Contains(e.SeccionId)) errores.Add($"Elemento {e.Id}: sección {e.SeccionId} inexistente");
        }
        foreach (var a in m.Apoyos)
            if (!ids.Contains(a.NodoId)) errores.Add($"Apoyo: nodo {a.NodoId} inexistente");
        foreach (var c in m.Cargas)
            if (!ids.Contains(c.NodoId)) errores.Add($"Carga: nodo {c.NodoId} inexistente");
        return errores;
    }

    private static IEnumerable<int> NodosApoyo(Edificio edificio, List<NodoFrame> nodos)
    {
        bool algunaZapata = edificio.Niveles.Any(nv => nv.Columnas.Any(c => c.Zapata != null));
        double cotaMin = edificio.Niveles.Count > 0 ? edificio.Niveles.Min(nv => nv.Cota) : 0.0;
        var resultado = new HashSet<int>();
        foreach (var nivel in edificio.Niveles)
            foreach (var c in nivel.Columnas)
            {
                bool fijar = algunaZapata ? c.Zapata != null : Math.Abs(nivel.Cota - cotaMin) < 1e-9;
                if (!fijar) continue;
                int? id = BuscarNodo(nodos, c.CoordenadaX, c.CoordenadaY, nivel.Cota);
                if (id is int v) resultado.Add(v);
            }
        return resultado;
    }

    private static int? BuscarNodo(List<NodoFrame> nodos, double x, double y, double z)
    {
        long mx = (long)Math.Round(x * 1000), my = (long)Math.Round(y * 1000), mz = (long)Math.Round(z * 1000);
        foreach (var n in nodos)
            if ((long)Math.Round(n.X * 1000) == mx
                && (long)Math.Round(n.Y * 1000) == my
                && (long)Math.Round(n.Z * 1000) == mz)
                return n.Id;
        return null;
    }
}

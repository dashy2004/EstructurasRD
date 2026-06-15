using System;
using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Vigas;

namespace LosasPlus.Services;

public readonly record struct NodoFrame(int Id, double X, double Y, double Z);

/// <summary>Barra sintetizada: conectividad + datos para derivar sección/material.</summary>
public readonly record struct ElementoFrame(
    int Id, int NodoI, int NodoJ, double B, double H, double Fc, bool EsColumna);

/// <summary>Sintetiza nodos+barras del pórtico (columnas+vigas) de un Edificio, deduplicando
/// nodos por milímetro. Z = Cota del nivel (eje vertical del motor). Convenciones de coordenadas
/// alineadas con src/Core/Render3D/EscenaEdificio.cs (columna: Cota→Cota+Altura; viga: Origen→Extremo).</summary>
public static class SintetizadorFrame
{
    public const double SeccionVigaBaseDefecto = 0.30;     // m
    public const double SeccionVigaPeralteDefecto = 0.50;  // m
    public const double FcDefecto = 0.210;                 // ton/cm² (≈21 MPa), default RD

    public static (List<NodoFrame> Nodos, List<ElementoFrame> Elementos) Sintetizar(Edificio edificio)
    {
        var nodos = new List<NodoFrame>();
        var indicePorClave = new Dictionary<(long, long, long), int>();
        var elementos = new List<ElementoFrame>();

        int ObtenerNodo(double x, double y, double z)
        {
            var clave = (Mm(x), Mm(y), Mm(z));
            if (indicePorClave.TryGetValue(clave, out int existente)) return existente;
            int id = nodos.Count + 1;
            nodos.Add(new NodoFrame(id, x, y, z));
            indicePorClave[clave] = id;
            return id;
        }

        foreach (var nivel in edificio.Niveles)
        {
            double fc = FcDelNivel(nivel);

            foreach (var c in nivel.Columnas)
            {
                int i = ObtenerNodo(c.CoordenadaX, c.CoordenadaY, nivel.Cota);
                int j = ObtenerNodo(c.CoordenadaX, c.CoordenadaY, nivel.Cota + c.Altura);
                if (i == j) continue; // columna degenerada (Altura 0)
                elementos.Add(new ElementoFrame(elementos.Count + 1, i, j, c.Base, c.Peralte, fc, true));
            }

            foreach (var v in nivel.Vigas)
            {
                int i = ObtenerNodo(v.OrigenX, v.OrigenY, nivel.Cota);
                int j = ObtenerNodo(v.ExtremoX, v.ExtremoY, nivel.Cota);
                if (i == j) continue; // viga degenerada (longitud 0)
                var (b, h) = SeccionViga(v);
                elementos.Add(new ElementoFrame(elementos.Count + 1, i, j, b, h, fc, false));
            }
        }

        return (nodos, elementos);
    }

    private static long Mm(double metros) => (long)Math.Round(metros * 1000.0);

    private static double FcDelNivel(Nivel nivel)
        => nivel.Sistemas.Count > 0 ? nivel.Sistemas[0].Fc : FcDefecto;

    private static (double B, double H) SeccionViga(Viga v)
    {
        if (v.Tramos.Count > 0 && v.Tramos[0].Base > 0 && v.Tramos[0].Peralte > 0)
            return (v.Tramos[0].Base, v.Tramos[0].Peralte);
        return (SeccionVigaBaseDefecto, SeccionVigaPeralteDefecto);
    }
}

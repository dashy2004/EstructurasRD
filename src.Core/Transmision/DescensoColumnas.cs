using System.Collections.Generic;
using LosasPlus.Models;

namespace LosasPlus.Transmision;

/// <summary>
/// Carga axial asignada a una columna y el lado de zapata que requiere.
/// </summary>
/// <param name="Columna">La columna.</param>
/// <param name="CargaAxial">Carga vertical asignada a la columna (misma unidad que la carga total repartida).</param>
/// <param name="LadoZapata">Lado de la zapata cuadrada predimensionada, en m.</param>
public readonly record struct CargaColumna(Columna Columna, double CargaAxial, double LadoZapata);

/// <summary>
/// Descenso de la carga del edificio a las columnas y sus zapatas (Fase J —
/// último tramo de la transmisión losa→viga→<b>columna→zapata</b>).
///
/// <para>
/// <b>Aproximación:</b> reparto <b>equitativo</b> de la carga total entre las
/// columnas. El reparto topológico real (por área tributaria de cada columna)
/// requiere las posiciones en planta de losas/vigas, que el modelo aún no
/// tiene; el reparto equitativo es una primera aproximación válida para
/// predimensionar. Cada columna recibe carga total / nº columnas y se le
/// predimensiona (o actualiza) su <see cref="Columna.Zapata"/> cuadrada con
/// <see cref="PredimZapata"/>.
/// </para>
///
/// <para>Tipo puro de dominio — multiplataforma y testeable.</para>
/// </summary>
public static class DescensoColumnas
{
    /// <summary>
    /// Reparte <paramref name="cargaTotalServicio"/> equitativamente entre
    /// <paramref name="columnas"/>, predimensiona la zapata cuadrada de cada una
    /// para la <paramref name="presionAdmisible"/> del terreno (creándola si no
    /// existe), y devuelve la carga axial y el lado de zapata por columna. Sin
    /// columnas o con carga no positiva no hace nada y devuelve lista vacía.
    /// </summary>
    public static IReadOnlyList<CargaColumna> RepartirEquitativo(
        IReadOnlyList<Columna> columnas, double cargaTotalServicio, double presionAdmisible)
    {
        var resultado = new List<CargaColumna>();
        if (columnas is null || columnas.Count == 0 || cargaTotalServicio <= 0)
            return resultado;

        double porColumna = cargaTotalServicio / columnas.Count;

        foreach (var columna in columnas)
        {
            var z = PredimZapata.Cuadrada(porColumna, presionAdmisible);
            columna.Zapata ??= new Zapata { Nombre = $"Z-{columna.Nombre}" };
            columna.Zapata.Ancho = z.LadoCuadrada;
            columna.Zapata.Largo = z.LadoCuadrada;
            resultado.Add(new CargaColumna(columna, porColumna, z.LadoCuadrada));
        }

        return resultado;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LosasPlus.Services;

/// <summary>
/// Una celda dentro de una <see cref="TxtTabla"/>. Mantiene el valor actual,
/// el valor original al parsear, y expone <see cref="Modificada"/> para que la
/// UI marque visualmente las celdas que el usuario tocó.
/// </summary>
public class TxtCelda : INotifyPropertyChanged
{
    private string _valor = "";
    private readonly string _valorOriginal;

    public TxtCelda(string original)
    {
        _valor = original ?? "";
        _valorOriginal = _valor;
    }

    /// <summary>Valor actual de la celda — bindeable bidireccionalmente al TextBox del DataGrid.</summary>
    public string Valor
    {
        get => _valor;
        set
        {
            if (_valor == value) return;
            _valor = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Modificada));
        }
    }

    /// <summary>Valor que tenía la celda al parsear el TXT original (read-only).</summary>
    public string ValorOriginal => _valorOriginal;

    /// <summary>True si el usuario editó esta celda (Valor != ValorOriginal).</summary>
    public bool Modificada => _valor != _valorOriginal;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Una fila parseada del TXT. Contiene una lista ordenada de <see cref="TxtCelda"/>
/// (una por columna) y mantiene la línea original para poder reconstruir el TXT
/// con espaciados respetados cuando el usuario no ha tocado nada.
/// </summary>
public class TxtFilaTabla
{
    public TxtFilaTabla(int numero, IReadOnlyList<string> celdas, string lineaOriginal)
    {
        Numero = numero;
        LineaOriginal = lineaOriginal ?? "";
        foreach (var c in celdas) Celdas.Add(new TxtCelda(c));
    }

    /// <summary>1-indexed.</summary>
    public int Numero { get; }

    /// <summary>Línea cruda original (con espacios) para preservar layout en líneas no editadas.</summary>
    public string LineaOriginal { get; }

    public ObservableCollection<TxtCelda> Celdas { get; } = new();

    /// <summary>True si alguna celda fue editada.</summary>
    public bool TieneModificaciones => Celdas.Any(c => c.Modificada);
}

/// <summary>
/// Representación tabular de un archivo .TXT de salida del motor Losas.exe.
/// Cada línea es una fila; las columnas se detectan dividiendo por agrupaciones
/// de 2+ espacios (típicas del formato de reporte de Perdomo). El número de
/// columnas se uniforma al máximo encontrado (filas más cortas se completan
/// con celdas vacías) para que la grilla sea rectangular.
///
/// <para>
/// La intención: permitir al ingeniero copiar fácilmente las celdas como
/// si fuera Excel (Ctrl+C con tabs) y editar valores in-place para comparar
/// con su Excel de referencia, sin perder el TXT original.
/// </para>
/// </summary>
public class TxtTabla
{
    /// <summary>Filas parseadas, en orden.</summary>
    public ObservableCollection<TxtFilaTabla> Filas { get; } = new();

    /// <summary>Máximo número de columnas observado (ancho de la grilla).</summary>
    public int CantidadColumnas { get; private set; }

    /// <summary>True si al menos una celda fue editada.</summary>
    public bool TieneModificaciones => Filas.Any(f => f.TieneModificaciones);

    /// <summary>
    /// Parsea el contenido completo del .TXT en una tabla. Las columnas se
    /// detectan dividiendo cada línea por <c>\s{2,}</c> (regex equivalente a
    /// "dos o más espacios"). Se preserva la línea cruda para reconstrucción.
    /// </summary>
    public static TxtTabla Parse(string texto)
    {
        var tabla = new TxtTabla();
        if (string.IsNullOrEmpty(texto)) return tabla;

        var lineas = texto.Replace("\r\n", "\n").Split('\n');
        int maxCols = 0;
        for (int i = 0; i < lineas.Length; i++)
        {
            var linea = lineas[i];
            // Trim solo derecha — la izquierda preserva indentación si el usuario lo necesita
            // pero para parseo de columnas tratamos los espacios iniciales como columna 0.
            var celdas = Regex.Split(linea.TrimEnd(), @"\s{2,}")
                              .Where(c => c.Length > 0)
                              .ToList();
            // Si la línea es totalmente vacía mantenemos 1 celda vacía para preservar el espacio.
            if (celdas.Count == 0) celdas.Add("");
            tabla.Filas.Add(new TxtFilaTabla(i + 1, celdas, linea));
            if (celdas.Count > maxCols) maxCols = celdas.Count;
        }
        tabla.CantidadColumnas = maxCols;
        return tabla;
    }

    /// <summary>
    /// Reconstruye el TXT a partir de la tabla. Para filas <b>sin modificaciones</b>
    /// emite la <see cref="TxtFilaTabla.LineaOriginal"/> intacta (preserva
    /// exactamente el layout del motor). Para filas modificadas emite las
    /// celdas separadas por tabulaciones (formato Excel-friendly).
    /// </summary>
    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var fila in Filas)
        {
            if (!fila.TieneModificaciones)
                sb.AppendLine(fila.LineaOriginal);
            else
                sb.AppendLine(string.Join("\t", fila.Celdas.Select(c => c.Valor)));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Exporta el TXT (renderizado) a un nuevo path siguiendo la convención
    /// "no sobreescribir el original": si <paramref name="originalPath"/> es
    /// <c>foo.txt</c>, devuelve y escribe en <c>foo.modificado.txt</c> en la
    /// misma carpeta. Si ya existe, agrega timestamp.
    /// </summary>
    public string ExportarPreservandoOriginal(string originalPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
            throw new ArgumentException("Path original requerido.", nameof(originalPath));

        var dir   = Path.GetDirectoryName(originalPath) ?? "";
        var stem  = Path.GetFileNameWithoutExtension(originalPath);
        var ext   = Path.GetExtension(originalPath);
        var path  = Path.Combine(dir, $"{stem}.modificado{ext}");
        if (File.Exists(path))
            path = Path.Combine(dir, $"{stem}.modificado.{DateTime.Now:yyyyMMdd-HHmmss}{ext}");

        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Render(), Encoding.GetEncoding(1252));
        return path;
    }
}

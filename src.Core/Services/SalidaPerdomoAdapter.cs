using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Adapter que convierte la salida del parser determinístico
/// (<see cref="ParsedOutput"/>) en una <see cref="SalidaPerdomo"/> — el
/// modelo asociado a un <see cref="Sistema"/> que <c>MemoriaPlus</c> consume
/// para renderear las tablas de momentos y armaduras dentro del bloque NIVEL.
///
/// <para>
/// El parser <c>TxtParser</c> ya extrae todos los datos del <c>.txt</c> de
/// <c>Losas.exe</c> (momentos, armaduras de vano X/Y, armaduras sobre apoyos
/// X/Y); este adapter solo los re-empaqueta en los <c>record</c> inmutables
/// de <see cref="SalidaPerdomo"/> y reporta qué losas del <c>.dl</c> faltaron.
/// </para>
/// </summary>
public static class SalidaPerdomoAdapter
{
    /// <summary>
    /// Construye una <see cref="SalidaPerdomo"/> a partir de un
    /// <see cref="ParsedOutput"/> y la lista de IDs de losa esperadas
    /// (típicamente <c>sistema.Losas.Select(l =&gt; l.Id)</c>).
    /// </summary>
    /// <param name="parsed">Salida del <c>TxtParser</c>.</param>
    /// <param name="archivoTxtPath">Path absoluto al <c>.txt</c> de origen (informativo, va al modelo).</param>
    /// <param name="losasEsperadas">IDs de losa del <c>.dl</c>. Las que no aparezcan en <paramref name="parsed"/> van a <see cref="SalidaPerdomo.LosasNoParseadas"/>.</param>
    public static SalidaPerdomo From(ParsedOutput parsed, string archivoTxtPath, IEnumerable<int> losasEsperadas)
    {
        if (parsed is null) throw new ArgumentNullException(nameof(parsed));
        if (losasEsperadas is null) throw new ArgumentNullException(nameof(losasEsperadas));

        var salida = new SalidaPerdomo
        {
            ArchivoTxt   = archivoTxtPath ?? "",
            FechaParseo  = DateTime.Now,
            VersionMotor = parsed.Version ?? "",
        };

        // Momentos: solo filas que tienen Tipo+Carga (lo que indica que fueron
        // pobladas por el bloque "CALCULO DE MOMENTOS"; las que están solo en
        // armaduras tienen Dx/Dy pero no Tipo).
        foreach (var l in parsed.PorLosa.Where(l => l.Tipo.HasValue && l.Carga.HasValue))
        {
            salida.Momentos.Add(new MomentoLosa(
                l.Id,
                l.Tipo!.Value,
                l.Carga!.Value,
                l.H  ?? 0,
                l.Lx ?? 0,
                l.Ly ?? 0,
                l.Mfx ?? 0,
                l.Mfy ?? 0,
                l.MSx ?? 0,
                l.MSy ?? 0));
        }

        // Armaduras de vano según X (centro de losa).
        foreach (var l in parsed.PorLosa.Where(l => l.Dx.HasValue && l.AsxReq.HasValue))
        {
            salida.ArmadurasXCentro.Add(new ArmaduraLosa(
                l.Id,
                l.Dx!.Value,
                l.Mux  ?? 0,
                l.AsxReq!.Value,
                l.DisponerX ?? "",
                l.AsxProv ?? 0));
        }

        // Armaduras de vano según Y (centro de losa).
        foreach (var l in parsed.PorLosa.Where(l => l.Dy.HasValue && l.AsyReq.HasValue))
        {
            salida.ArmadurasYCentro.Add(new ArmaduraLosa(
                l.Id,
                l.Dy!.Value,
                l.Muy  ?? 0,
                l.AsyReq!.Value,
                l.DisponerY ?? "",
                l.AsyProv ?? 0));
        }

        // Armaduras sobre apoyos: TxtParser etiqueta cada ApoyoResult con su
        // dirección ("X" o "Y") leyendo qué cabecera vio antes; respetamos eso.
        foreach (var a in parsed.Apoyos.Where(a => string.Equals(a.Direccion, "X", StringComparison.OrdinalIgnoreCase)))
            salida.ArmadurasXApoyos.Add(MapApoyo(a));

        foreach (var a in parsed.Apoyos.Where(a => string.Equals(a.Direccion, "Y", StringComparison.OrdinalIgnoreCase)))
            salida.ArmadurasYApoyos.Add(MapApoyo(a));

        // Reportar IDs de losa esperados que NO aparecieron en el .txt — útil
        // para warnings tipo "3 losas no parseadas: L4, L7, L11".
        var idsParseados = new HashSet<int>(parsed.PorLosa.Select(l => l.Id));
        foreach (var id in losasEsperadas)
            if (!idsParseados.Contains(id))
                salida.LosasNoParseadas.Add(id);

        return salida;
    }

    /// <summary>
    /// Conveniencia: parsea el <c>.txt</c> de <paramref name="path"/> usando
    /// la decodificación cp1252 que produce <c>Losas.exe</c>, y lo adapta a
    /// <see cref="SalidaPerdomo"/> en una sola llamada.
    /// </summary>
    public static SalidaPerdomo FromFile(string path, IEnumerable<int> losasEsperadas)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Archivo .txt no encontrado: {path}", path);

        var parsed = TxtParser.ParseFile(path);
        return From(parsed, path, losasEsperadas);
    }

    private static ArmaduraApoyo MapApoyo(ApoyoResult a) => new(
        a.BordeI,
        a.BordeJ,
        a.MuI ?? 0,
        a.MuJ ?? 0,
        a.MuIJ ?? 0,
        a.D   ?? 0,
        a.As  ?? 0,
        a.AsI ?? 0,
        a.AsJ ?? 0,
        a.DAs ?? 0,
        a.Disponer ?? "");
}

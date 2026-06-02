using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LosasPlus.Models;

/// <summary>
/// Salida del software F. Perdomo (Losas v5.20) parseada y asociada a un
/// <see cref="Sistema"/>. El parser concreto vive en
/// <c>LosasPlus.Services.TxtParser</c> (Core) y rellena estos campos a partir
/// del archivo <c>.txt</c> exportado por la GUI nativa de <c>Losas.exe</c>.
///
/// <para>
/// Memoria Plus consume esta información para:
/// </para>
/// <list type="number">
///   <item>Validar coherencia entre el <c>.dl</c> editado y la salida (cantidad de
///         losas, dimensiones).</item>
///   <item>Renderizar las tablas de momentos y armaduras dentro de la sección
///         "Diseño de Losas: Nivel X" del <c>.docx</c>.</item>
///   <item>Insertar el bloque del software (re-renderizado con estilo de la
///         plantilla) bajo cada nivel de la memoria.</item>
/// </list>
///
/// <para>
/// Esta clase es un esqueleto: las colecciones de momentos y armaduras quedan
/// como placeholders <see cref="MomentoLosa"/> y <see cref="ArmaduraLosa"/>
/// hasta que el parser extendido (<c>TxtParser</c> + bloques de armaduras
/// X/Y centro y X/Y apoyos) aterrice en un commit posterior.
/// </para>
/// </summary>
public class SalidaPerdomo : INotifyPropertyChanged
{
    private string _archivoTxt = "";
    private DateTime _fechaParseo = DateTime.MinValue;
    private string _versionMotor = "";

    /// <summary>Path absoluto al archivo <c>.txt</c> importado.</summary>
    public string ArchivoTxt
    {
        get => _archivoTxt;
        set { _archivoTxt = value; OnPropertyChanged(); }
    }

    /// <summary>Fecha en que el archivo fue parseado por Memoria Plus.</summary>
    public DateTime FechaParseo
    {
        get => _fechaParseo;
        set { _fechaParseo = value; OnPropertyChanged(); }
    }

    /// <summary>Versión del motor reportada por el .txt (e.g. "Losas v5.20").</summary>
    public string VersionMotor
    {
        get => _versionMotor;
        set { _versionMotor = value; OnPropertyChanged(); }
    }

    /// <summary>Tabla de momentos flectores por losa (ton·m/m).</summary>
    public ObservableCollection<MomentoLosa> Momentos { get; } = new();

    /// <summary>Armaduras de vano según X (centro de losa).</summary>
    public ObservableCollection<ArmaduraLosa> ArmadurasXCentro { get; } = new();

    /// <summary>Armaduras de vano según Y (centro de losa).</summary>
    public ObservableCollection<ArmaduraLosa> ArmadurasYCentro { get; } = new();

    /// <summary>Armaduras sobre apoyos según X.</summary>
    public ObservableCollection<ArmaduraApoyo> ArmadurasXApoyos { get; } = new();

    /// <summary>Armaduras sobre apoyos según Y.</summary>
    public ObservableCollection<ArmaduraApoyo> ArmadurasYApoyos { get; } = new();

    /// <summary>
    /// IDs de losa presentes en el <c>.dl</c> que no fueron encontradas en el
    /// <c>.txt</c>. Se reportan como warning al importar.
    /// </summary>
    public List<int> LosasNoParseadas { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Una fila de la tabla "Cálculo de Momentos" del .txt: dimensiones y
/// momentos flectores en X e Y, positivos (vano) y negativos (apoyo).
/// </summary>
public sealed record MomentoLosa(
    int    LosaId,
    int    Tipo,
    double Carga,    // ton/m²
    double H,        // m
    double Lx,       // m
    double Ly,       // m
    double Mfx,      // momento flector X positivo (ton·m/m)
    double Mfy,      // momento flector Y positivo
    double NMSx,     // momento flector X negativo (apoyo)
    double NMSy);    // momento flector Y negativo (apoyo)

/// <summary>
/// Una fila de "Armaduras según X/Y" — sección centro de losa.
/// </summary>
public sealed record ArmaduraLosa(
    int    LosaId,
    double D,        // canto útil (m)
    double Mu,       // momento último (ton·m/m)
    double As,       // área de acero requerida (cm²/m)
    string Disponer, // descripción colocada (e.g. "Ø10 c/18")
    double AsReal);  // área de acero efectivamente colocada (cm²/m)

/// <summary>
/// Una fila de "Armaduras según X/Y" — sección sobre apoyos (borde I-J).
/// </summary>
public sealed record ArmaduraApoyo(
    int    BordeI,
    int    BordeJ,
    double MuI,      // momento último en losa I
    double MuJ,      // momento último en losa J
    double MuIJ,     // momento balanceado entre I y J
    double D,
    double As,
    double AsI,      // área en lado I
    double AsJ,      // área en lado J
    double DAs,      // diferencia / detalle
    string Disponer);

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using LosasPlus.Calculo;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

/// <summary>
/// ViewModel de la pestaña "Aceros" (A1). Para cada <see cref="Losa"/> del
/// <see cref="Sistema"/> activo que ya tenga momentos importados del <c>.TXT</c>
/// (<see cref="Losa.Mfx"/>/<see cref="Losa.Mfy"/>/<see cref="Losa.MSx"/>/<see cref="Losa.MSy"/>),
/// computa el diseño de acero a flexión por franja con
/// <see cref="AcerosLosaDesigner"/> y lo expone como filas planas para un DataGrid.
///
/// <para>Sin dependencias de Avalonia — testeable. Recibe el sistema por un
/// <c>Func</c> (mismo patrón que <see cref="ColumnasEditorViewModel"/>). El
/// recubrimiento y la barra supuesta son editables y recalculan en vivo.</para>
/// </summary>
public sealed class AcerosViewModel : INotifyPropertyChanged
{
    private readonly Func<Sistema?> _getSistema;

    public AcerosViewModel(Func<Sistema?> getSistema)
    {
        _getSistema = getSistema ?? throw new ArgumentNullException(nameof(getSistema));
        Recargar();
    }

    /// <summary>Filas de diseño: una por (losa × franja) de las losas con momentos.</summary>
    public ObservableCollection<DisenoAceroFila> Filas { get; } = new();

    /// <summary>Grupos de diseño por losa, para la UI.</summary>
    public ObservableCollection<LosaAcerosGrupo> Grupos { get; } = new();

    private double _recubrimientoCm = AcerosLosaDesigner.RecubrimientoDefaultCm;
    /// <summary>Recubrimiento libre (cm) usado en el cálculo del canto útil. Default 2.5.</summary>
    public double RecubrimientoCm
    {
        get => _recubrimientoCm;
        set { if (Math.Abs(_recubrimientoCm - value) < 1e-9) return; _recubrimientoCm = value; OnPropertyChanged(); Recargar(); }
    }

    private int _barraSupuesta = 4;
    /// <summary>Número de barra supuesto para estimar el canto útil (db). Default #4.</summary>
    public int BarraSupuesta
    {
        get => _barraSupuesta;
        set { if (_barraSupuesta == value) return; _barraSupuesta = value; OnPropertyChanged(); Recargar(); }
    }

    /// <summary>True si alguna losa del sistema tiene momentos importados.</summary>
    public bool HayMomentos => Filas.Count > 0;

    /// <summary>Total de franjas evaluadas.</summary>
    public int TotalFranjas => Filas.Count;

    /// <summary>Cantidad de franjas que NO cumplen (sección insuficiente o espaciamiento fuera de norma).</summary>
    public int FranjasNoCumplen => Filas.Count(f => !f.Cumple);

    /// <summary>Mensaje guía cuando no hay momentos: hay que importar el .TXT primero.</summary>
    public string MensajeVacio =>
        "Importá la salida .TXT del motor (Salida .TXT) para que cada losa tenga "
        + "momentos; el diseño de acero aparece automáticamente acá.";

    /// <summary>Recalcula todas las filas desde el sistema activo.</summary>
    public void Recargar()
    {
        Filas.Clear();
        Grupos.Clear();
        var sistema = _getSistema();
        if (sistema is not null)
        {
            foreach (var losa in sistema.Losas.Where(TieneMomentos))
            {
                var ml = new MomentoLosa(
                    losa.Id, losa.Tipo, losa.Carga, losa.Espesor, losa.Lx, losa.Ly,
                    losa.Mfx ?? 0, losa.Mfy ?? 0, losa.MSx ?? 0, losa.MSy ?? 0);

                var d = AcerosLosaDesigner.DisenarLosa(
                    ml, sistema.Fc, sistema.Fy, RecubrimientoCm, BarraSupuesta);

                double hCm = losa.Espesor * 100.0;
                var fxc = new DisenoAceroFila(losa.Id, losa.Tipo, d.XCentro, hCm, SyncToPerdomo);
                var fyc = new DisenoAceroFila(losa.Id, losa.Tipo, d.YCentro, hCm, SyncToPerdomo);
                var fxa = new DisenoAceroFila(losa.Id, losa.Tipo, d.XApoyo, hCm, SyncToPerdomo);
                var fya = new DisenoAceroFila(losa.Id, losa.Tipo, d.YApoyo, hCm, SyncToPerdomo);

                Filas.Add(fxc);
                Filas.Add(fyc);
                Filas.Add(fxa);
                Filas.Add(fya);

                var grupo = new LosaAcerosGrupo(losa.Id, losa.Tipo, losa.Lx, losa.Ly, losa.Espesor);
                grupo.Franjas.Add(fxc);
                grupo.Franjas.Add(fyc);
                grupo.Franjas.Add(fxa);
                grupo.Franjas.Add(fya);
                Grupos.Add(grupo);
            }
        }
        OnPropertyChanged(nameof(HayMomentos));
        OnPropertyChanged(nameof(TotalFranjas));
        OnPropertyChanged(nameof(FranjasNoCumplen));
    }

    private void SyncToPerdomo(DisenoAceroFila fila)
    {
        var sistema = _getSistema();
        if (sistema?.SalidaPerdomo is null) return;

        ObservableCollection<ArmaduraLosa>? targetList = null;
        if (fila.Franja.StartsWith("X") && fila.Franja.Contains("centro", StringComparison.OrdinalIgnoreCase))
            targetList = sistema.SalidaPerdomo.ArmadurasXCentro;
        else if (fila.Franja.StartsWith("Y") && fila.Franja.Contains("centro", StringComparison.OrdinalIgnoreCase))
            targetList = sistema.SalidaPerdomo.ArmadurasYCentro;

        if (targetList != null)
        {
            var target = targetList.FirstOrDefault(a => a.LosaId == fila.LosaId);
            if (target != null)
            {
                var newTarget = target with { Disponer = fila.Disponer, AsReal = fila.AsProvistaCm2M };
                int idx = targetList.IndexOf(target);
                targetList[idx] = newTarget;
            }
            else
            {
                targetList.Add(new ArmaduraLosa(fila.LosaId, fila.DCm / 100.0, fila.MuTonM, fila.AsRequeridoCm2M, fila.Disponer, fila.AsProvistaCm2M));
            }
        }
    }


    private static bool TieneMomentos(Losa l)
        => l.Mfx.HasValue || l.Mfy.HasValue || l.MSx.HasValue || l.MSy.HasValue;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Fila plana de diseño de acero para la grilla: identifica la losa y aplana
/// el <see cref="DisenoAceroFranja"/> a propiedades amigables de binding.
/// </summary>
public sealed class DisenoAceroFila : INotifyPropertyChanged
{
    private DisenoAceroFranja _f;
    private readonly double _hCm;
    private readonly Action<DisenoAceroFila>? _onOverride;

    public DisenoAceroFila(int losaId, int tipo, DisenoAceroFranja franja, double hCm, Action<DisenoAceroFila>? onOverride = null)
    {
        LosaId = losaId;
        Tipo = tipo;
        _f = franja;
        _hCm = hCm;
        _onOverride = onOverride;
    }

    public int LosaId { get; }
    public int Tipo { get; }
    public string Franja => _f.Franja;
    public double MuTonM => _f.MuTonM;
    public double DCm => _f.DCm;
    public double AsRequeridoCm2M => _f.AsRequeridoCm2M;
    public double AsMinimoCm2M => _f.AsMinimoCm2M;
    public double AsDisenoCm2M => _f.AsDisenoCm2M;
    public double AsProvistaCm2M => _f.AsProvistaCm2M;
    
    public int NumeroBarra
    {
        get => _f.NumeroBarra;
        set => AplicarOverride(value, _f.EspaciamientoCm);
    }
    
    public double EspaciamientoCm
    {
        get => _f.EspaciamientoCm;
        set => AplicarOverride(_f.NumeroBarra, value);
    }
    
    public string Disponer => _f.Disponer;
    public bool SeccionInsuficiente => _f.SeccionInsuficiente;
    public bool GobiernaMinimo => _f.GobiernaMinimo;

    /// <summary>True si el ingeniero editó manualmente la barra/espaciamiento de esta franja.</summary>
    public bool EsManual { get; private set; }

    /// <summary>True si la franja está resuelta correctamente (sección OK y espaciamiento normativo).</summary>
    public bool Cumple => !_f.SeccionInsuficiente && _f.CumpleEspaciamiento;

    /// <summary>Estado textual para la grilla: "OK", "REVISAR" o "SECCIÓN INSUF.".</summary>
    public string Estado => _f.SeccionInsuficiente ? "SECCIÓN INSUF." : (Cumple ? "OK" : "REVISAR");

    /// <summary>
    /// Edita el acero dispuesto de esta franja (barra + espaciamiento): recalcula
    /// el acero provisto y si cumple, y la marca como manual. La grilla se refresca
    /// vía <see cref="PropertyChanged"/>. No cambia el acero requerido.
    /// </summary>
    public void AplicarOverride(int numeroBarra, double espaciamientoCm)
    {
        if (numeroBarra < 3 || numeroBarra > 8 || espaciamientoCm < 7.5) return;
        _f = AcerosLosaDesigner.AplicarOverride(
            _f, numeroBarra, espaciamientoCm, AcerosLosaDesigner.EspaciamientoMaximo(_hCm));
        EsManual = true;
        foreach (var p in new[]
        {
            nameof(AsProvistaCm2M), nameof(NumeroBarra), nameof(EspaciamientoCm),
            nameof(Disponer), nameof(Cumple), nameof(Estado), nameof(EsManual),
        })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
            
        _onOverride?.Invoke(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class LosaAcerosGrupo
{
    public int LosaId { get; }
    public int Tipo { get; }
    public double Lx { get; }
    public double Ly { get; }
    public double Espesor { get; }
    public ObservableCollection<DisenoAceroFila> Franjas { get; } = new();

    public LosaAcerosGrupo(int losaId, int tipo, double lx, double ly, double h)
    {
        LosaId = losaId;
        Tipo = tipo;
        Lx = lx;
        Ly = ly;
        Espesor = h;
    }
}


using LosasPlus.Calculo;
using LosasPlus.Models;

namespace LosasPlus.ViewModels;

public class ZapataDisenoRow
{
    public Columna Columna { get; set; } = default!;
    public ZapataDisenador.DisenoZapata Diseno { get; set; } = default!;
    
    public string NombreColumna => Columna.Nombre;
    public double PuTon { get; set; }
    
    // Resultados formatados para UI
    public string QuStr => $"{Diseno.QuMPa:F3} MPa";
    
    public double PunzRatio => Diseno.Punzonamiento.Ratio;
    public bool PunzCumple => Diseno.Punzonamiento.Cumple;
    public string PunzColor => PunzCumple ? "#4CAF50" : "#F44336";
    
    public double CortRatio => Diseno.Cortante.Ratio;
    public bool CortCumple => Diseno.Cortante.Cumple;
    public string CortColor => CortCumple ? "#4CAF50" : "#F44336";
    
    public string MuStr => $"{(Diseno.MuNmm / 1e6):F2} kN·m";
    
    public string AsStr => $"{Diseno.Acero.AsReqMm2:F0} / {Diseno.Acero.AsMinMm2:F0} mm²";
    public bool AsCumple => !Diseno.Acero.SeccionInsuficiente;
    public string AsColor => AsCumple ? "#4CAF50" : "#F44336";
    
    public bool GlobalCumple => Diseno.Cumple;
    public string GlobalCumpleStr => GlobalCumple ? "CUMPLE" : "FALLA";
    public string GlobalColor => GlobalCumple ? "#4CAF50" : "#F44336";
}

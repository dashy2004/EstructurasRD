using System.Diagnostics;
using System.IO;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class ExportadorIntegracionMotorTests
{
    private const string PythonMotor =
        "/home/gdc/Downloads/EstructurasRD-engine/motor-fea/.venv/bin/python";
    private const string DirMotor =
        "/home/gdc/Downloads/EstructurasRD-engine/motor-fea";

    private static Edificio PorticoConZapatas()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });
        (double x, double y)[] esq = { (0, 0), (5, 0), (5, 5), (0, 5) };
        foreach (var (x, y) in esq)
            nivel.Columnas.Add(new Columna
            {
                CoordenadaX = x, CoordenadaY = y, Base = 0.30, Peralte = 0.30, Altura = 3.0,
                Zapata = new Zapata(),
            });
        (double ox, double oy, double ang)[] vigas = { (0, 0, 0), (5, 0, 90), (5, 5, 180), (0, 5, 270) };
        foreach (var (ox, oy, ang) in vigas)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50 });
            nivel.Vigas.Add(v);
        }
        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void El_modelo_exportado_es_resoluble_por_el_motor()
    {
        if (!File.Exists(PythonMotor)) return; // guardado: motor no disponible

        string json = ExportadorModeloMotor.ToJson(ExportadorModeloMotor.Exportar(PorticoConZapatas()));

        var psi = new ProcessStartInfo(PythonMotor)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = DirMotor,
        };
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("motor_fea.api.cli");
        psi.ArgumentList.Add("--analyze");
        psi.ArgumentList.Add("-");

        using var p = Process.Start(psi)!;
        p.StandardInput.Write(json);
        p.StandardInput.Close();
        string salida = p.StandardOutput.ReadToEnd();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(30000);

        Assert.True(p.ExitCode == 0, $"El motor falló (exit {p.ExitCode}): {err}");
        Assert.False(string.IsNullOrWhiteSpace(salida)); // produjo resultados → no singular
    }
}

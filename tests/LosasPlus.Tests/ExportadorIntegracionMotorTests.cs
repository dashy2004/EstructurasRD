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

    [Fact]
    public void El_motor_acepta_un_modelo_con_losas()
    {
        if (!File.Exists(PythonMotor)) return; // guardado

        var ed = PorticoConZapatas();
        var nivel = ed.Niveles[0];
        Sistema sis = nivel.Sistemas.Count > 0 ? nivel.Sistemas[0] : null!;
        if (sis is null) { sis = new Sistema { Fc = 0.210, Fy = 4.200 }; nivel.Sistemas.Add(sis); }
        sis.Losas.Add(new Losa { CoordenadaX = 0, CoordenadaY = 0, Lx = 5, Ly = 5, Espesor = 0.12 });

        string json = ExportadorModeloMotor.ToJson(ExportadorModeloMotor.Exportar(ed));
        Assert.Contains("\"losas\"", json);
        Assert.Contains("\"puntos\"", json);

        var psi = new ProcessStartInfo(PythonMotor)
        {
            ArgumentList = { "-m", "motor_fea.api.cli", "--analyze", "-" },
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = DirMotor,
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(json);
        p.StandardInput.Close();
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        Assert.True(p.ExitCode == 0, $"El motor rechazó el modelo con losas (exit {p.ExitCode}): {err}");
    }
}

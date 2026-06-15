using System.Text.Json;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class ModeloMotorModelsTests
{
    [Fact]
    public void Serializa_con_las_claves_exactas_del_contrato_del_motor()
    {
        var m = new ModeloMotorDto
        {
            Nodos = { new NodoMotor { Id = 1, X = 0, Y = 0, Z = 0 } },
            Materiales = { new MaterialMotor { Id = 1, E = 2.0e10, Nu = 0.2, Densidad = 2400 } },
            Secciones = { new SeccionMotor { Id = 1, Area = 0.09, InerciaY = 0.000675, InerciaZ = 0.000675, ConstanteTorsion = 0.00114 } },
            Elementos = { new ElementoMotor { Id = 1, NodoI = 1, NodoJ = 2, MaterialId = 1, SeccionId = 1 } },
            Apoyos = { new ApoyoMotor { NodoId = 1, Ux = true, Uy = true, Uz = true, Rx = true, Ry = true, Rz = true } },
        };

        string json = JsonSerializer.Serialize(m);

        Assert.Contains("\"nodos\"", json);
        Assert.Contains("\"materiales\"", json);
        Assert.Contains("\"secciones\"", json);
        Assert.Contains("\"elementos\"", json);
        Assert.Contains("\"apoyos\"", json);
        Assert.Contains("\"cargas\"", json);
        Assert.Contains("\"nodo_i\"", json);
        Assert.Contains("\"material_id\"", json);
        Assert.Contains("\"inercia_y\"", json);
        Assert.Contains("\"constante_torsion\"", json);
        Assert.Contains("\"vector_referencia\"", json);
        Assert.Contains("[0,0,1]", json.Replace(" ", ""));
    }
}

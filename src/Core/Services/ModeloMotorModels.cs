using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LosasPlus.Services;

/// <summary>Modelo estructural en el contrato JSON del motor (entrada de --analyze/visor).
/// Las claves JSON deben calzar EXACTO con motor_fea/api/contrato.py.</summary>
public sealed class ModeloMotorDto
{
    [JsonPropertyName("nodos")]      public List<NodoMotor> Nodos { get; set; } = new();
    [JsonPropertyName("materiales")] public List<MaterialMotor> Materiales { get; set; } = new();
    [JsonPropertyName("secciones")]  public List<SeccionMotor> Secciones { get; set; } = new();
    [JsonPropertyName("elementos")]  public List<ElementoMotor> Elementos { get; set; } = new();
    [JsonPropertyName("apoyos")]     public List<ApoyoMotor> Apoyos { get; set; } = new();
    [JsonPropertyName("cargas")]     public List<CargaMotor> Cargas { get; set; } = new();
}

public sealed class NodoMotor
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("x")]  public double X { get; set; }
    [JsonPropertyName("y")]  public double Y { get; set; }
    [JsonPropertyName("z")]  public double Z { get; set; }
}

public sealed class MaterialMotor
{
    [JsonPropertyName("id")]       public int Id { get; set; }
    [JsonPropertyName("E")]        public double E { get; set; }
    [JsonPropertyName("nu")]       public double Nu { get; set; }
    [JsonPropertyName("densidad")] public double Densidad { get; set; }
}

public sealed class SeccionMotor
{
    [JsonPropertyName("id")]                public int Id { get; set; }
    [JsonPropertyName("area")]              public double Area { get; set; }
    [JsonPropertyName("inercia_y")]         public double InerciaY { get; set; }
    [JsonPropertyName("inercia_z")]         public double InerciaZ { get; set; }
    [JsonPropertyName("constante_torsion")] public double ConstanteTorsion { get; set; }
}

public sealed class ElementoMotor
{
    [JsonPropertyName("id")]                public int Id { get; set; }
    [JsonPropertyName("nodo_i")]            public int NodoI { get; set; }
    [JsonPropertyName("nodo_j")]            public int NodoJ { get; set; }
    [JsonPropertyName("material_id")]       public int MaterialId { get; set; }
    [JsonPropertyName("seccion_id")]        public int SeccionId { get; set; }
    [JsonPropertyName("vector_referencia")] public double[] VectorReferencia { get; set; } = new[] { 0.0, 0.0, 1.0 };
}

public sealed class ApoyoMotor
{
    [JsonPropertyName("nodo_id")] public int NodoId { get; set; }
    [JsonPropertyName("ux")] public bool Ux { get; set; }
    [JsonPropertyName("uy")] public bool Uy { get; set; }
    [JsonPropertyName("uz")] public bool Uz { get; set; }
    [JsonPropertyName("rx")] public bool Rx { get; set; }
    [JsonPropertyName("ry")] public bool Ry { get; set; }
    [JsonPropertyName("rz")] public bool Rz { get; set; }
}

public sealed class CargaMotor
{
    [JsonPropertyName("nodo_id")] public int NodoId { get; set; }
    [JsonPropertyName("fx")] public double Fx { get; set; }
    [JsonPropertyName("fy")] public double Fy { get; set; }
    [JsonPropertyName("fz")] public double Fz { get; set; }
    [JsonPropertyName("mx")] public double Mx { get; set; }
    [JsonPropertyName("my")] public double My { get; set; }
    [JsonPropertyName("mz")] public double Mz { get; set; }
}

using System.Text.Json.Serialization;

namespace LosasPlus.Services;

/// <summary>Parámetros de entrada de <c>--disenar-losa</c>. Las claves JSON deben calzar exacto
/// con el contrato del motor (ojo: <c>E</c> mayúscula).</summary>
public sealed class ParamsLosaMotor
{
    [JsonPropertyName("a")]            public double A { get; set; }
    [JsonPropertyName("b")]            public double B { get; set; }
    [JsonPropertyName("nx")]           public int    Nx { get; set; }
    [JsonPropertyName("ny")]           public int    Ny { get; set; }
    [JsonPropertyName("E")]            public double E { get; set; }
    [JsonPropertyName("nu")]           public double Nu { get; set; }
    [JsonPropertyName("t")]            public double T { get; set; }
    [JsonPropertyName("q")]            public double Q { get; set; }
    [JsonPropertyName("fc")]           public double Fc { get; set; }
    [JsonPropertyName("fy")]           public double Fy { get; set; }
    [JsonPropertyName("recubrimiento")] public double Recubrimiento { get; set; }
    [JsonPropertyName("borde")]        public string Borde { get; set; } = "simple";
}

/// <summary>Franja de armado devuelta por el motor.</summary>
public sealed class FranjaMotor
{
    [JsonPropertyName("as_requerido")] public double AsRequerido { get; set; }
    [JsonPropertyName("as_minimo")]    public double AsMinimo { get; set; }
    [JsonPropertyName("as_diseno")]    public double AsDiseno { get; set; }
    [JsonPropertyName("numero_barra")] public int? NumeroBarra { get; set; }
    [JsonPropertyName("espaciamiento")] public double Espaciamiento { get; set; }
    [JsonPropertyName("as_provista")]  public double AsProvista { get; set; }
    [JsonPropertyName("cumple")]       public bool Cumple { get; set; }
    [JsonPropertyName("disponer")]     public string? Disponer { get; set; }
}

/// <summary>Salida de <c>--disenar-losa</c> para una losa.</summary>
public sealed class ResultadoLosaMotor
{
    [JsonPropertyName("w_central")]   public double WCentral { get; set; }
    [JsonPropertyName("mx_max")]      public double MxMax { get; set; }
    [JsonPropertyName("my_max")]      public double MyMax { get; set; }
    [JsonPropertyName("m_apoyo_max")] public double MApoyoMax { get; set; }
    [JsonPropertyName("mu_x")]        public double MuX { get; set; }
    [JsonPropertyName("mu_y")]        public double MuY { get; set; }
    [JsonPropertyName("mu_apoyo")]    public double MuApoyo { get; set; }
    [JsonPropertyName("franja_x")]    public FranjaMotor FranjaX { get; set; } = new();
    [JsonPropertyName("franja_y")]    public FranjaMotor FranjaY { get; set; } = new();
    [JsonPropertyName("franja_apoyo")] public FranjaMotor FranjaApoyo { get; set; } = new();
}

/// <summary>Resultado de ejecutar el proceso del motor.</summary>
public sealed record ResultadoProceso(int ExitCode, string Stdout, string Stderr);

/// <summary>Error al ejecutar/leer el motor para una losa.</summary>
public sealed class MotorFeaException : System.Exception
{
    public MotorFeaException(string message) : base(message) { }
}

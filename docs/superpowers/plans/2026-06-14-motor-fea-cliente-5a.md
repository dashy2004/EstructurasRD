# Motor-FEA como cliente de losas (#5a) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir un camino opcional por el cual MemoriaPlus invoque el motor Python (`--disenar-losa`) para diseñar cada losa del nivel activo y mostrar los resultados en la vista existente, sin tocar el camino de `Losas.exe` (Perdomo), que sigue por defecto.

**Architecture:** Tres servicios puros/aislados en `Core` (mapeo Losa→params, cliente de proceso, adaptador JSON→`ParsedOutput`) + un orquestador testeable + un comando en `MainViewModel`. El downstream se reutiliza: traducimos el JSON del motor al intermedio `ParsedOutput` y llamamos `SalidaPerdomoAdapter.From(...)` sin cambios, de modo que la tabla de resultados existente funciona igual.

**Tech Stack:** C# / .NET 8, namespace raíz `LosasPlus` (servicios en `LosasPlus.Services`, modelos en `LosasPlus.Models`), `System.Text.Json` 8.0.5, xUnit 2.9 (proyecto `tests/LosasPlus.Tests`), Avalonia 11.3 (MVVM manual con `RelayCommand`/`AsyncRelayCommand`).

---

## Contexto imprescindible (leer antes de empezar)

**Spec:** `docs/superpowers/specs/2026-06-14-motor-fea-cliente-5a-design.md`.

**Contrato del motor** (`--disenar-losa -`, JSON por stdin → JSON por stdout, exit 1 en error):
- **Entrada (params):** `{ "a", "b", "nx", "ny", "E", "nu", "t", "q", "fc", "fy", "recubrimiento", "borde" }`. Unidades SI: `a,b,t` en m; `E` en Pa; `q` en N/m²; `fc,fy` en MPa; `recubrimiento` en mm; `borde` ∈ `{"simple","empotrado"}`. Ojo: la clave del módulo elástico es **`E` mayúscula**, el resto minúsculas.
- **Salida:** `{ "w_central", "mx_max", "my_max", "m_apoyo_max", "mu_x", "mu_y", "mu_apoyo", "franja_x", "franja_y", "franja_apoyo" }`. Momentos `mx_max/my_max/m_apoyo_max` en N·m/m; `mu_*` en N·mm/m; `w_central` en m. Cada `franja_*` = `{ "as_requerido", "as_minimo", "as_diseno", "seccion_insuficiente", "gobierna_minimo", "numero_barra", "espaciamiento", "as_provista", "cumple", "disponer" }`; `as_*` en mm²/m, `espaciamiento` en mm, `disponer` p.ej. `"#5 @ 150"`.

**Comando del motor (dev venv, D2):** ejecutable `/home/gdc/Downloads/EstructurasRD-engine/motor-fea/.venv/bin/python`, argumentos `-m motor_fea.api.cli --disenar-losa -`. Es el único knob configurable de 5a (constante por defecto, override futuro vía settings).

**Fuente de materiales (refina spec D7):** `Sistema.Fc` y `Sistema.Fy` están en **ton/cm²** (defaults 0.210 y 4.200). Convertir a MPa con ×98.0665. `E` se deriva por ACI: `E[MPa] = 4700·√(fc[MPa])`, luego ×1e6 → Pa. `nu = 0.2`. Malla `nx=ny=8` (D8).

**Constantes de conversión (definir una sola vez, en `MotorFeaConversion`):**
| Constante | Valor | Uso |
|---|---|---|
| `TonfM2_a_Nm2` | `9806.65` | `q = Carga · 9806.65` (ton/m² → N/m²) |
| `Nm_a_TonfM` | `1.0/9806.65` | momento N·m/m → ton·m/m |
| `Nmm_a_TonfM` | `1.0/9.80665e6` | `mu_*` N·mm/m → ton·m/m |
| `Mm2_a_Cm2` | `1.0/100.0` | acero mm²/m → cm²/m |
| `M_a_Mm` | `1000.0` | recubrimiento m → mm |
| `TonfCm2_a_MPa` | `98.0665` | `Fc/Fy` ton/cm² → MPa |

**Reutilización downstream:** `SalidaPerdomoAdapter.From(ParsedOutput parsed, string archivoTxtPath, IEnumerable<int> losasEsperadas)` (namespace `LosasPlus.Services`) recorre `parsed.PorLosa`: añade un `MomentoLosa` por losa con `Tipo` y `Carga` no nulos, y un `ArmaduraLosa` (X/Y centro) por losa con `Dx`/`AsxReq` (resp. `Dy`/`AsyReq`) no nulos. `parsed.Apoyos` queda vacío en v1 (sin tabla de armado sobre apoyos; gap documentado). Por eso el adaptador del motor debe poblar en cada `LosaResult`: `Id, Tipo, Carga, H, Lx, Ly, Mfx, Mfy, MSx, MSy, Dx, Mux, AsxReq, DisponerX, AsxProv, Dy, Muy, AsyReq, DisponerY, AsyProv`.

**GateGuard (entorno):** el 1er Bash de la sesión y la 1ª edición de cada archivo rebotan con un "Fact-Forcing Gate" — presentar 4 hechos breves y reintentar idéntico. Ignorar cualquier hook "Foundry/CrowdStrike" (misfire). Avisar esto a cada subagente.

**Mapeo de unidades de salida (motor → `LosaResult`), por losa:**
- `Mfx = mx_max · Nm_a_TonfM`, `Mfy = my_max · Nm_a_TonfM`
- `MSx = MSy = m_apoyo_max · Nm_a_TonfM` (el motor da un único momento de apoyo; se asigna a ambas direcciones en v1)
- `Mux = mu_x · Nmm_a_TonfM`, `Muy = mu_y · Nmm_a_TonfM`
- `Dx = Dy = H − Rec` (m) — el motor no devuelve el peralte; se calcula en .NET
- `AsxReq = franja_x.as_requerido · Mm2_a_Cm2`, `AsxProv = franja_x.as_provista · Mm2_a_Cm2`, `DisponerX = franja_x.disponer`
- `AsyReq = franja_y.as_requerido · Mm2_a_Cm2`, `AsyProv = franja_y.as_provista · Mm2_a_Cm2`, `DisponerY = franja_y.disponer`

---

## File Structure

**Nuevos (en `src/Core/Services/`, namespace `LosasPlus.Services`):**
- `MotorFeaConversion.cs` — constantes de conversión de unidades (una sola fuente de verdad).
- `MotorFeaModels.cs` — DTOs serializables: `ParamsLosaMotor`, `ResultadoLosaMotor`, `FranjaMotor`, `ResultadoProceso`, excepción `MotorFeaException`.
- `MapeadorLosaMotor.cs` — puro: `Losa` + `Sistema` (Fc/Fy) + `borde` → `ParamsLosaMotor` (con conversiones app→SI).
- `IProcesoRunner.cs` + `ProcesoRunner.cs` — abstracción de ejecución de proceso (stdin/stdout/exit) + impl real con `System.Diagnostics.Process`.
- `MotorFeaClient.cs` — usa `IProcesoRunner`: envía params JSON, devuelve stdout JSON, lanza `MotorFeaException` en exit ≠ 0.
- `MotorFeaAdapter.cs` — puro: JSON de salida del motor + `Losa` → `LosaResult`.
- `MotorFeaLosasService.cs` — orquestador testeable: recorre las losas (mapeador → client → adapter → `ParsedOutput`), llama `SalidaPerdomoAdapter.From`, devuelve `(SalidaPerdomo, List<int> fallidas)`.

**Modificados:**
- `src/MemoriaPlus/ViewModels/MainViewModel.cs` — comando `CalcularLosasConMotorCommand` + método `CalcularLosasConMotor` + propiedades `StatusMotor` y `BordeLosaMotor`, junto a `ImportarTxtPerdomo`.
- La vista de MemoriaPlus que bindea `ImportarTxtPerdomoCommand` — botón "Calcular losas con el motor (FEA)", selector de borde y label de status.

**Tests (en `tests/LosasPlus.Tests/`, namespace `LosasPlus.Tests`, xUnit):**
- `MapeadorLosaMotorTests.cs`, `MotorFeaAdapterTests.cs`, `MotorFeaClientTests.cs`, `MotorFeaLosasServiceTests.cs`.

---

## Task 1: Constantes de conversión + DTOs + Mapeador Losa→params

**Files:**
- Create: `src/Core/Services/MotorFeaConversion.cs`
- Create: `src/Core/Services/MotorFeaModels.cs`
- Create: `src/Core/Services/MapeadorLosaMotor.cs`
- Test: `tests/LosasPlus.Tests/MapeadorLosaMotorTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/LosasPlus.Tests/MapeadorLosaMotorTests.cs`:

```csharp
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MapeadorLosaMotorTests
{
    private static (Sistema sis, Losa losa) Demo()
    {
        var sis = new Sistema { Fc = 0.210, Fy = 4.200 }; // ton/cm²
        var losa = new Losa { Id = 1, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 };
        sis.Losas.Add(losa);
        return (sis, losa);
    }

    [Fact]
    public void Mapea_geometria_y_malla()
    {
        var (sis, losa) = Demo();
        var p = MapeadorLosaMotor.Map(losa, sis, "simple");
        Assert.Equal(5.0, p.A, 6);
        Assert.Equal(5.0, p.B, 6);
        Assert.Equal(0.20, p.T, 6);
        Assert.Equal(8, p.Nx);
        Assert.Equal(8, p.Ny);
        Assert.Equal(0.2, p.Nu, 6);
        Assert.Equal("simple", p.Borde);
    }

    [Fact]
    public void Convierte_unidades_app_a_SI()
    {
        var (sis, losa) = Demo();
        var p = MapeadorLosaMotor.Map(losa, sis, "empotrado");
        Assert.Equal(25.0, p.Recubrimiento, 3);          // 0.025 m → mm
        Assert.Equal(9806.65, p.Q, 2);                   // 1.0 ton/m² → N/m²
        Assert.Equal(20.594, p.Fc, 3);                   // 0.210 ton/cm² → MPa
        Assert.Equal(411.879, p.Fy, 3);                  // 4.200 ton/cm² → MPa
        Assert.Equal("empotrado", p.Borde);
    }

    [Fact]
    public void Deriva_E_por_ACI_en_Pa()
    {
        var (sis, losa) = Demo();
        var p = MapeadorLosaMotor.Map(losa, sis, "simple");
        // E = 4700·√(20.594) · 1e6 ≈ 2.1329e10 Pa
        Assert.Equal(2.1329e10, p.E, -7); // tolerancia ~1e7 vía precisión negativa de redondeo
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MapeadorLosaMotorTests`
Expected: FAIL de compilación — `MapeadorLosaMotor`/`ParamsLosaMotor` no existen.

- [ ] **Step 3: Write the conversion constants**

Create `src/Core/Services/MotorFeaConversion.cs`:

```csharp
namespace LosasPlus.Services;

/// <summary>Constantes de conversión entre las unidades de la app (Perdomo) y las del motor (SI).</summary>
public static class MotorFeaConversion
{
    /// <summary>ton/m² → N/m². 1 tonf = 1000 kgf, 1 kgf = 9.80665 N.</summary>
    public const double TonfM2_a_Nm2 = 9806.65;

    /// <summary>N·m/m → ton·m/m.</summary>
    public const double Nm_a_TonfM = 1.0 / 9806.65;

    /// <summary>N·mm/m → ton·m/m.</summary>
    public const double Nmm_a_TonfM = 1.0 / 9.80665e6;

    /// <summary>mm²/m → cm²/m.</summary>
    public const double Mm2_a_Cm2 = 1.0 / 100.0;

    /// <summary>m → mm.</summary>
    public const double M_a_Mm = 1000.0;

    /// <summary>ton/cm² → MPa. 1 tonf/cm² = 1000 kgf/cm² = 98.0665 MPa.</summary>
    public const double TonfCm2_a_MPa = 98.0665;

    /// <summary>Módulo elástico del hormigón por ACI 318: E[MPa] = 4700·√(fc[MPa]).</summary>
    public static double ModuloElasticoPa(double fcMPa) => 4700.0 * System.Math.Sqrt(fcMPa) * 1e6;
}
```

- [ ] **Step 4: Write the DTOs**

Create `src/Core/Services/MotorFeaModels.cs`:

```csharp
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
    [JsonPropertyName("numero_barra")] public string? NumeroBarra { get; set; }
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
```

- [ ] **Step 5: Write the mapper**

Create `src/Core/Services/MapeadorLosaMotor.cs`:

```csharp
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Traduce una <see cref="Losa"/> de la app a los parámetros SI que espera el motor.</summary>
public static class MapeadorLosaMotor
{
    public const int MallaPorDefecto = 8;
    public const double NuHormigon = 0.2;

    public static ParamsLosaMotor Map(Losa losa, Sistema sistema, string borde)
    {
        double fcMPa = sistema.Fc * MotorFeaConversion.TonfCm2_a_MPa;
        double fyMPa = sistema.Fy * MotorFeaConversion.TonfCm2_a_MPa;
        return new ParamsLosaMotor
        {
            A = losa.Lx,
            B = losa.Ly,
            Nx = MallaPorDefecto,
            Ny = MallaPorDefecto,
            E = MotorFeaConversion.ModuloElasticoPa(fcMPa),
            Nu = NuHormigon,
            T = losa.Espesor,
            Q = losa.Carga * MotorFeaConversion.TonfM2_a_Nm2,
            Fc = fcMPa,
            Fy = fyMPa,
            Recubrimiento = losa.Rec * MotorFeaConversion.M_a_Mm,
            Borde = borde,
        };
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MapeadorLosaMotorTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Core/Services/MotorFeaConversion.cs src/Core/Services/MotorFeaModels.cs src/Core/Services/MapeadorLosaMotor.cs tests/LosasPlus.Tests/MapeadorLosaMotorTests.cs
git commit -m "feat(#5a): mapeador Losa→params del motor + DTOs + conversión de unidades"
```

---

## Task 2: Adaptador JSON del motor → LosaResult

**Files:**
- Create: `src/Core/Services/MotorFeaAdapter.cs`
- Test: `tests/LosasPlus.Tests/MotorFeaAdapterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/LosasPlus.Tests/MotorFeaAdapterTests.cs`:

```csharp
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MotorFeaAdapterTests
{
    // Salida representativa del motor (losa 5×5, borde simple).
    private const string JsonMotor = """
    {
      "w_central": 0.00123,
      "mx_max": 5234.5, "my_max": 5210.3, "m_apoyo_max": 0.0,
      "mu_x": 5234500.0, "mu_y": 5210300.0, "mu_apoyo": 0.0,
      "franja_x": { "as_requerido": 485.5, "as_minimo": 400.0, "as_diseno": 485.5,
                    "numero_barra": "#5", "espaciamiento": 150, "as_provista": 500.0,
                    "cumple": true, "disponer": "#5 @ 150" },
      "franja_y": { "as_requerido": 480.0, "as_minimo": 400.0, "as_diseno": 480.0,
                    "numero_barra": "#5", "espaciamiento": 150, "as_provista": 500.0,
                    "cumple": true, "disponer": "#5 @ 150" },
      "franja_apoyo": { "as_requerido": 0.0, "as_minimo": 400.0, "as_diseno": 400.0,
                    "numero_barra": "#4", "espaciamiento": 200, "as_provista": 400.0,
                    "cumple": true, "disponer": "#4 @ 200" }
    }
    """;

    private static Losa DemoLosa() =>
        new Losa { Id = 7, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 };

    [Fact]
    public void Convierte_momentos_y_geometria()
    {
        var r = MotorFeaAdapter.Map(JsonMotor, DemoLosa());
        Assert.Equal(7, r.Id);
        Assert.Equal(1, r.Tipo);
        Assert.Equal(1.0, r.Carga!.Value, 6);
        Assert.Equal(0.20, r.H!.Value, 6);
        Assert.Equal(5.0, r.Lx!.Value, 6);
        Assert.Equal(0.534, r.Mfx!.Value, 3);   // 5234.5 / 9806.65
        Assert.Equal(0.531, r.Mfy!.Value, 3);   // 5210.3 / 9806.65
        Assert.Equal(0.0,   r.MSx!.Value, 3);   // m_apoyo_max = 0 (simple)
    }

    [Fact]
    public void Convierte_armado_X_y_Y()
    {
        var r = MotorFeaAdapter.Map(JsonMotor, DemoLosa());
        Assert.Equal(0.175, r.Dx!.Value, 3);    // H - Rec = 0.20 - 0.025
        Assert.Equal(0.534, r.Mux!.Value, 3);   // 5234500 / 9.80665e6
        Assert.Equal(4.855, r.AsxReq!.Value, 3); // 485.5 / 100
        Assert.Equal(5.0,   r.AsxProv!.Value, 3); // 500 / 100
        Assert.Equal("#5 @ 150", r.DisponerX);
        Assert.Equal(4.80,  r.AsyReq!.Value, 3); // 480 / 100
        Assert.Equal("#5 @ 150", r.DisponerY);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MotorFeaAdapterTests`
Expected: FAIL de compilación — `MotorFeaAdapter` no existe.

- [ ] **Step 3: Write the adapter**

Create `src/Core/Services/MotorFeaAdapter.cs`:

```csharp
using System.Text.Json;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Traduce el JSON de salida de <c>--disenar-losa</c> (una losa) al intermedio
/// <see cref="LosaResult"/> que consume <see cref="SalidaPerdomoAdapter"/>, convirtiendo SI → app.</summary>
public static class MotorFeaAdapter
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static LosaResult Map(string jsonSalida, Losa losa)
    {
        var m = JsonSerializer.Deserialize<ResultadoLosaMotor>(jsonSalida, Opts)
                ?? throw new MotorFeaException($"Salida del motor vacía o inválida para la losa {losa.Id}.");
        return Map(m, losa);
    }

    public static LosaResult Map(ResultadoLosaMotor m, Losa losa)
    {
        double d = losa.Espesor - losa.Rec; // peralte efectivo (m)
        return new LosaResult
        {
            Id = losa.Id,
            Tipo = losa.Tipo,
            Carga = losa.Carga,
            H = losa.Espesor,
            Lx = losa.Lx,
            Ly = losa.Ly,
            Mfx = m.MxMax * MotorFeaConversion.Nm_a_TonfM,
            Mfy = m.MyMax * MotorFeaConversion.Nm_a_TonfM,
            MSx = m.MApoyoMax * MotorFeaConversion.Nm_a_TonfM,
            MSy = m.MApoyoMax * MotorFeaConversion.Nm_a_TonfM,
            Dx = d,
            Mux = m.MuX * MotorFeaConversion.Nmm_a_TonfM,
            AsxReq = m.FranjaX.AsRequerido * MotorFeaConversion.Mm2_a_Cm2,
            AsxProv = m.FranjaX.AsProvista * MotorFeaConversion.Mm2_a_Cm2,
            DisponerX = m.FranjaX.Disponer,
            Dy = d,
            Muy = m.MuY * MotorFeaConversion.Nmm_a_TonfM,
            AsyReq = m.FranjaY.AsRequerido * MotorFeaConversion.Mm2_a_Cm2,
            AsyProv = m.FranjaY.AsProvista * MotorFeaConversion.Mm2_a_Cm2,
            DisponerY = m.FranjaY.Disponer,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MotorFeaAdapterTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/MotorFeaAdapter.cs tests/LosasPlus.Tests/MotorFeaAdapterTests.cs
git commit -m "feat(#5a): adaptador JSON del motor → LosaResult (SI → app)"
```

---

## Task 3: Runner de proceso + cliente del motor

**Files:**
- Create: `src/Core/Services/IProcesoRunner.cs`
- Create: `src/Core/Services/ProcesoRunner.cs`
- Create: `src/Core/Services/MotorFeaClient.cs`
- Test: `tests/LosasPlus.Tests/MotorFeaClientTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/LosasPlus.Tests/MotorFeaClientTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MotorFeaClientTests
{
    private sealed class FakeRunner : IProcesoRunner
    {
        public ResultadoProceso Resultado = new(0, "{}", "");
        public string? StdinRecibido;
        public Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct)
        {
            StdinRecibido = stdin;
            return Task.FromResult(Resultado);
        }
    }

    [Fact]
    public async Task Exito_devuelve_stdout_y_envia_params_por_stdin()
    {
        var fake = new FakeRunner { Resultado = new(0, "{\"mx_max\":1.0}", "") };
        var client = new MotorFeaClient(fake);
        var salida = await client.DisenarLosaAsync("{\"a\":5}", CancellationToken.None);
        Assert.Equal("{\"mx_max\":1.0}", salida);
        Assert.Equal("{\"a\":5}", fake.StdinRecibido);
    }

    [Fact]
    public async Task ExitCode_distinto_de_cero_lanza_MotorFeaException_con_stderr()
    {
        var fake = new FakeRunner { Resultado = new(1, "", "boom: parámetros inválidos") };
        var client = new MotorFeaClient(fake);
        var ex = await Assert.ThrowsAsync<MotorFeaException>(
            () => client.DisenarLosaAsync("{}", CancellationToken.None));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task Stdout_vacio_con_exit_cero_lanza_MotorFeaException()
    {
        var fake = new FakeRunner { Resultado = new(0, "   ", "") };
        var client = new MotorFeaClient(fake);
        await Assert.ThrowsAsync<MotorFeaException>(
            () => client.DisenarLosaAsync("{}", CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MotorFeaClientTests`
Expected: FAIL de compilación — `IProcesoRunner`/`MotorFeaClient` no existen.

- [ ] **Step 3: Write the runner abstraction**

Create `src/Core/Services/IProcesoRunner.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Services;

/// <summary>Ejecuta un proceso externo enviando <paramref name="stdin"/> y capturando stdout/stderr/exit.</summary>
public interface IProcesoRunner
{
    Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct);
}
```

Create `src/Core/Services/ProcesoRunner.cs`:

```csharp
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Services;

/// <summary>Impl real sobre <see cref="Process"/> con stdin/stdout/stderr redirigidos.</summary>
public sealed class ProcesoRunner : IProcesoRunner
{
    public async Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ejecutable,
            Arguments = argumentos,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = new Process { StartInfo = psi };
        p.Start();

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
        await p.WaitForExitAsync(ct);

        return new ResultadoProceso(p.ExitCode, await stdoutTask, await stderrTask);
    }
}
```

- [ ] **Step 4: Write the client**

Create `src/Core/Services/MotorFeaClient.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Services;

/// <summary>Cliente del motor Python: envía los params de una losa por stdin y devuelve el JSON de salida.</summary>
public sealed class MotorFeaClient
{
    /// <summary>Comando por defecto (dev venv del motor, decisión D2). Único knob configurable de 5a.</summary>
    public const string EjecutablePorDefecto =
        "/home/gdc/Downloads/EstructurasRD-engine/motor-fea/.venv/bin/python";
    public const string ArgumentosPorDefecto = "-m motor_fea.api.cli --disenar-losa -";

    private readonly IProcesoRunner _runner;
    private readonly string _ejecutable;
    private readonly string _argumentos;

    public MotorFeaClient(IProcesoRunner runner, string? ejecutable = null, string? argumentos = null)
    {
        _runner = runner;
        _ejecutable = ejecutable ?? EjecutablePorDefecto;
        _argumentos = argumentos ?? ArgumentosPorDefecto;
    }

    public async Task<string> DisenarLosaAsync(string paramsJson, CancellationToken ct)
    {
        ResultadoProceso r;
        try
        {
            r = await _runner.EjecutarAsync(_ejecutable, _argumentos, paramsJson, ct);
        }
        catch (System.Exception ex)
        {
            throw new MotorFeaException($"No se pudo ejecutar el motor ('{_ejecutable}'): {ex.Message}");
        }
        if (r.ExitCode != 0)
            throw new MotorFeaException($"El motor terminó con código {r.ExitCode}: {r.Stderr.Trim()}");
        if (string.IsNullOrWhiteSpace(r.Stdout))
            throw new MotorFeaException("El motor no produjo salida.");
        return r.Stdout;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MotorFeaClientTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Services/IProcesoRunner.cs src/Core/Services/ProcesoRunner.cs src/Core/Services/MotorFeaClient.cs tests/LosasPlus.Tests/MotorFeaClientTests.cs
git commit -m "feat(#5a): runner de proceso abstraído + MotorFeaClient (stdin/stdout/exit)"
```

---

## Task 4: Orquestador — sistema completo → SalidaPerdomo

**Files:**
- Create: `src/Core/Services/MotorFeaLosasService.cs`
- Test: `tests/LosasPlus.Tests/MotorFeaLosasServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/LosasPlus.Tests/MotorFeaLosasServiceTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MotorFeaLosasServiceTests
{
    private const string JsonOk = """
    { "w_central":0.001, "mx_max":5234.5, "my_max":5210.3, "m_apoyo_max":0.0,
      "mu_x":5234500.0, "mu_y":5210300.0, "mu_apoyo":0.0,
      "franja_x":{"as_requerido":485.5,"as_minimo":400,"as_diseno":485.5,"numero_barra":"#5","espaciamiento":150,"as_provista":500,"cumple":true,"disponer":"#5 @ 150"},
      "franja_y":{"as_requerido":480.0,"as_minimo":400,"as_diseno":480.0,"numero_barra":"#5","espaciamiento":150,"as_provista":500,"cumple":true,"disponer":"#5 @ 150"},
      "franja_apoyo":{"as_requerido":0,"as_minimo":400,"as_diseno":400,"numero_barra":"#4","espaciamiento":200,"as_provista":400,"cumple":true,"disponer":"#4 @ 200"} }
    """;

    // Runner que devuelve OK siempre, salvo cuando el stdin contiene "a": 99 (losa marcada para fallar).
    private sealed class Runner : IProcesoRunner
    {
        public int Llamadas;
        public Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct)
        {
            Llamadas++;
            bool falla = stdin.Contains("\"a\":99") || stdin.Contains("\"a\": 99");
            return Task.FromResult(falla ? new ResultadoProceso(1, "", "borde inválido") : new ResultadoProceso(0, JsonOk, ""));
        }
    }

    private static Sistema SistemaCon(params Losa[] losas)
    {
        var s = new Sistema { Nombre = "Nivel 1", Fc = 0.210, Fy = 4.200 };
        foreach (var l in losas) s.Losas.Add(l);
        return s;
    }

    [Fact]
    public async Task Calcula_todas_las_losas_y_puebla_SalidaPerdomo()
    {
        var sis = SistemaCon(
            new Losa { Id = 1, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 },
            new Losa { Id = 2, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 6.0, Rec = 0.025 });
        var svc = new MotorFeaLosasService(new MotorFeaClient(new Runner()));

        var (salida, fallidas) = await svc.CalcularAsync(sis, "simple", CancellationToken.None);

        Assert.Empty(fallidas);
        Assert.Equal(2, salida.Momentos.Count);
        Assert.Equal(2, salida.ArmadurasXCentro.Count);
        Assert.Equal(2, salida.ArmadurasYCentro.Count);
        Assert.Contains("motor", salida.ArchivoTxt); // fuente marcada
    }

    [Fact]
    public async Task Una_losa_que_falla_se_reporta_y_no_corta_la_corrida()
    {
        var sis = SistemaCon(
            new Losa { Id = 1, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 },
            new Losa { Id = 2, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 99.0, Ly = 6.0, Rec = 0.025 }); // Lx=99 → "a":99 → falla
        var svc = new MotorFeaLosasService(new MotorFeaClient(new Runner()));

        var (salida, fallidas) = await svc.CalcularAsync(sis, "simple", CancellationToken.None);

        Assert.Single(fallidas);
        Assert.Equal(2, fallidas[0]);
        Assert.Single(salida.Momentos); // solo la losa 1
    }
}
```

> Nota: el mapeador serializa `A = losa.Lx`; con `System.Text.Json` por defecto sin indentación el stdin contiene `"a":99` (sin espacio). El test cubre ambas formas por robustez.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MotorFeaLosasServiceTests`
Expected: FAIL de compilación — `MotorFeaLosasService` no existe.

- [ ] **Step 3: Write the orchestrator**

Create `src/Core/Services/MotorFeaLosasService.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Orquesta el diseño de todas las losas de un <see cref="Sistema"/> vía el motor:
/// mapeo → cliente → adaptador → <see cref="ParsedOutput"/> → <see cref="SalidaPerdomoAdapter"/>.</summary>
public sealed class MotorFeaLosasService
{
    /// <summary>Marcador de origen en <see cref="SalidaPerdomo.ArchivoTxt"/> cuando la fuente es el motor.</summary>
    public const string FuenteMotor = "motor-fea (FEA)";

    private readonly MotorFeaClient _client;

    public MotorFeaLosasService(MotorFeaClient client) => _client = client;

    /// <summary>Calcula todas las losas y devuelve la <see cref="SalidaPerdomo"/> poblada
    /// y la lista de ids de losas que fallaron (omitidas, no cortan la corrida).</summary>
    public async Task<(SalidaPerdomo salida, List<int> fallidas)> CalcularAsync(
        Sistema sistema, string borde, CancellationToken ct)
    {
        var parsed = new ParsedOutput { Sistema = sistema.Nombre };
        var fallidas = new List<int>();

        foreach (var losa in sistema.Losas)
        {
            try
            {
                var prm = MapeadorLosaMotor.Map(losa, sistema, borde);
                string json = System.Text.Json.JsonSerializer.Serialize(prm);
                string salidaJson = await _client.DisenarLosaAsync(json, ct);
                parsed.PorLosa.Add(MotorFeaAdapter.Map(salidaJson, losa));
            }
            catch (MotorFeaException)
            {
                fallidas.Add(losa.Id);
            }
        }

        var ids = sistema.Losas.Select(l => l.Id);
        var salida = SalidaPerdomoAdapter.From(parsed, FuenteMotor, ids);
        return (salida, fallidas);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~MotorFeaLosasServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Run the full suite (no regressions)**

Run: `dotnet test tests/LosasPlus.Tests`
Expected: PASS (suite existente + 10 tests nuevos).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Services/MotorFeaLosasService.cs tests/LosasPlus.Tests/MotorFeaLosasServiceTests.cs
git commit -m "feat(#5a): orquestador Sistema→SalidaPerdomo vía motor (reusa SalidaPerdomoAdapter)"
```

---

## Task 5: Comando en MainViewModel

**Files:**
- Modify: `src/MemoriaPlus/ViewModels/MainViewModel.cs`

> No hay test unitario de VM (es UI Avalonia); la lógica está cubierta por `MotorFeaLosasServiceTests`. El VM solo cablea.

- [ ] **Step 1: Add the status + border properties**

En `MainViewModel.cs`, junto a la propiedad `StatusImportarTxt`, añadir:

```csharp
private string _statusMotor = "";
/// <summary>Mensaje del último cálculo con el motor FEA del nivel activo.</summary>
public string StatusMotor
{
    get => _statusMotor;
    set { _statusMotor = value; OnPropertyChanged(); }
}

private string _bordeLosaMotor = "simple";
/// <summary>Condición de borde para el motor: "simple" (apoyo simple, por defecto) o "empotrado".</summary>
public string BordeLosaMotor
{
    get => _bordeLosaMotor;
    set { _bordeLosaMotor = value; OnPropertyChanged(); }
}
```

- [ ] **Step 2: Declare the command property**

Junto a `public RelayCommand QuitarTxtPerdomoCommand { get; }`, añadir:

```csharp
public AsyncRelayCommand CalcularLosasConMotorCommand { get; }
```

- [ ] **Step 3: Bind the command in the constructor**

En el constructor, junto a `ImportarTxtPerdomoCommand = new AsyncRelayCommand(ImportarTxtPerdomo, () => SistemaActivo != null);`, añadir:

```csharp
CalcularLosasConMotorCommand = new AsyncRelayCommand(CalcularLosasConMotor,
    () => SistemaActivo != null && SistemaActivo.Losas.Count > 0);
```

Y en el setter de `SistemaActivo`, junto a las otras llamadas `…?.RaiseCanExecuteChanged();`, añadir:

```csharp
CalcularLosasConMotorCommand?.RaiseCanExecuteChanged();
```

- [ ] **Step 4: Add the command method**

Junto al método `ImportarTxtPerdomo`, añadir:

```csharp
private async Task CalcularLosasConMotor()
{
    if (SistemaActivo is null) return;
    try
    {
        StatusMotor = "Calculando con el motor…";
        var svc = new MotorFeaLosasService(new MotorFeaClient(new ProcesoRunner()));
        var (salida, fallidas) = await svc.CalcularAsync(
            SistemaActivo, BordeLosaMotor, System.Threading.CancellationToken.None);

        SistemaActivo.SalidaPerdomo = salida;

        StatusMotor = fallidas.Count == 0
            ? $"Motor: {salida.Momentos.Count} losa(s) calculadas (borde {BordeLosaMotor})."
            : $"Motor: {salida.Momentos.Count} calculadas, {fallidas.Count} fallida(s): " +
              string.Join(", ", fallidas.Select(id => $"L{id}"));

        QuitarTxtPerdomoCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(SistemaActivo));
    }
    catch (Exception ex)
    {
        StatusMotor = $"Error ejecutando el motor: {ex.Message}";
    }
}
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build src/MemoriaPlus/MemoriaPlus.App.csproj`
Expected: Build succeeded (0 errores).

- [ ] **Step 6: Commit**

```bash
git add src/MemoriaPlus/ViewModels/MainViewModel.cs
git commit -m "feat(#5a): comando CalcularLosasConMotor en MainViewModel (borde + status)"
```

---

## Task 6: Botón + selector de borde en la vista

**Files:**
- Modify: la vista de MemoriaPlus que bindea `ImportarTxtPerdomoCommand` (buscarla).

- [ ] **Step 1: Locate the view**

Run: `grep -rl "ImportarTxtPerdomoCommand" src/MemoriaPlus --include=*.axaml`
Expected: un `.axaml` (el panel F. Perdomo del nivel). Abrirlo y localizar el botón "Importar .txt".

- [ ] **Step 2: Add the controls next to the import button**

Junto al botón que bindea `ImportarTxtPerdomoCommand`, añadir (ajustar el contenedor al layout existente — `StackPanel`/`Grid`):

```xml
<StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
  <Button Content="Calcular losas con el motor (FEA)"
          Command="{Binding CalcularLosasConMotorCommand}" />
  <ComboBox SelectedItem="{Binding BordeLosaMotor}" Width="140">
    <ComboBoxItem Content="simple" />
    <ComboBoxItem Content="empotrado" />
  </ComboBox>
</StackPanel>
<TextBlock Text="{Binding StatusMotor}" TextWrapping="Wrap" Margin="0,4,0,0" />
```

> Si el binding del `ComboBox` da fricción en esta versión de Avalonia (los `ComboBoxItem` enlazan el control, no el string), reemplazar por items de string directos: `<ComboBox SelectedItem="{Binding BordeLosaMotor}"><x:String>simple</x:String><x:String>empotrado</x:String></ComboBox>` (requiere `xmlns:x`). El valor enlazado debe terminar siendo `"simple"` o `"empotrado"`.

- [ ] **Step 3: Build + run to verify visually**

Run: `dotnet build src/MemoriaPlus/MemoriaPlus.App.csproj`
Expected: Build succeeded.

Verificación manual (requiere el motor en `~/Downloads/EstructurasRD-engine/motor-fea/.venv`):
1. `dotnet run --project src/MemoriaPlus`
2. Abrir/crear un proyecto con un nivel que tenga losas (con `Tipo`, `Carga`, `Lx`, `Ly`, `Espesor`, `Rec`).
3. Pulsar "Calcular losas con el motor (FEA)" con borde "simple".
4. Confirmar: el status reporta N losas; la tabla de resultados (la misma de Perdomo) muestra momentos y armado.
5. Probar "empotrado": cambian momentos/deflexión y aparece momento de apoyo (MSx/MSy ≠ 0).

- [ ] **Step 4: Commit**

```bash
git add src/MemoriaPlus
git commit -m "feat(#5a): UI — botón motor FEA + selector de borde + status en el panel del nivel"
```

---

## Self-Review (cobertura del spec)

- **§1 objetivo / definición de hecho:** comando (T5/T6), invocación CLI por losa (T3/T4), traducción a `SalidaPerdomo` reusando el adaptador (T2/T4), borde elegible (T5/T6), errores por losa sin caer (T3/T4), tests verdes (T1–T4). ✓
- **§3 decisiones:** D1 backend opcional (Perdomo intacto, T4–T6) · D2 dev venv configurable (`MotorFeaClient` consts, T3) · D3 un subproceso por losa (T4 loop) · D4 borde por corrida, defecto simple (T5) · D5 unidades (T1/T2 + `MotorFeaConversion`) · D6 fallidas sin caer (T3/T4) · D7 materiales: **refinado a `Sistema.Fc/Fy` ton/cm² → MPa** (T1) · D8 malla 8 (T1) · D9 puebla `SalidaPerdomo` en memoria, marca fuente `motor-fea` (T4). ✓
- **§6 conversión de unidades:** centralizada en `MotorFeaConversion`, ejercida por T1 (app→SI) y T2 (SI→app). ✓
- **§7 errores:** exit≠0/stdout vacío/no ejecutable → `MotorFeaException` (T3); por losa → lista `fallidas` (T4). Timeout opcional NO incluido en v1 (YAGNI; `CancellationToken` ya se propaga si se quiere añadir después). ✓
- **§8 tests:** mapeador (T1), adaptador golden JSON (T2), cliente con runner mock sin Python (T3), orquestador (T4). Test de integración con binario real **no** automatizado; cubierto por la verificación manual de T6 (decisión de simplicidad: evita acoplar la suite a un venv externo). ✓
- **Gaps documentados (§10):** 23 tipos→borde único, notación de despiece pass-through, N arranques de Python, apoyos sin tabla en v1 — todos intencionales.

**Type consistency:** `ParamsLosaMotor`, `ResultadoLosaMotor`, `FranjaMotor`, `ResultadoProceso`, `MotorFeaException` (T1) → usados igual en T2/T3/T4. `MapeadorLosaMotor.Map`, `MotorFeaAdapter.Map`, `MotorFeaClient.DisenarLosaAsync`, `MotorFeaLosasService.CalcularAsync` con firmas consistentes entre tareas. `SalidaPerdomoAdapter.From(ParsedOutput, string, IEnumerable<int>)` y los nombres de campo de `LosaResult`/`Losa`/`Sistema` verificados contra el código real.

**Desviación del spec a comunicar:** D7 se concretó — la fuente de f'c/fy es `Sistema.Fc`/`Sistema.Fy` (ton/cm²), no un default genérico; fy resultante ≈ 411.9 MPa (la app guarda 4.200 ton/cm² ≈ 4200 kg/cm²), no 420 MPa exacto.

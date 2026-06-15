# Exportador de modelo al motor · Etapa 1a (pórtico → visor) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir al escritorio un comando que exporte el pórtico (columnas + vigas) del edificio activo al JSON de modelo del motor, para que el visor WebXR muestre la geometría 3D del edificio.

**Architecture:** Tres piezas puras en `Core` (DTOs del contrato, geometría de sección, sintetizador de nodos/barras) + un ensamblador (`ExportadorModeloMotor`) + un escritor a archivo + un comando en el ViewModel. El motor (otro repo) no se toca. Espejo estructural de #5a (cliente de losas).

**Tech Stack:** C# / .NET 8 (file-scoped namespaces, nullable enable), `System.Text.Json`, xUnit 2.9.2 (`[Fact]`, `Assert.Equal(expected, actual, precision)`), Avalonia (`AsyncRelayCommand`, `AppServices.Dialogs`).

**Spec:** `docs/superpowers/specs/2026-06-14-exportador-modelo-motor-portico-design.md`

**Convenciones del repo (verificadas):**
- Core: proyecto `src/Core/LosasPlus.Core.csproj`, namespaces `LosasPlus.Models`, `LosasPlus.Services`, `LosasPlus.Vigas`.
- Tests: `tests/LosasPlus.Tests/`, namespace `LosasPlus.Tests`, xUnit. Filtro: `dotnet test --filter FullyQualifiedName~<Clase>`.
- Contrato motor (destino, SI, Z arriba): `nodos{id,x,y,z}`, `materiales{id,E,nu,densidad}`, `secciones{id,area,inercia_y,inercia_z,constante_torsion}`, `elementos{id,nodo_i,nodo_j,material_id,seccion_id,vector_referencia}`, `apoyos{nodo_id,ux,uy,uz,rx,ry,rz}`, `cargas{nodo_id,fx,fy,fz,mx,my,mz}`.
- Reuso: `LosasPlus.Services.MotorFeaConversion` ya tiene `TonfCm2_a_MPa = 98.0665` y `ModuloElasticoPa(fcMPa) = 4700·√fc·1e6`.

**Estructura de archivos (decisiones de descomposición):**
| Archivo | Responsabilidad |
|---|---|
| `src/Core/Services/ModeloMotorModels.cs` | DTOs serializables del contrato del motor (sin lógica). |
| `src/Core/Services/GeometriaSeccion.cs` | Cálculo puro de propiedades de sección rectangular (A, Iy, Iz, J). |
| `src/Core/Services/SintetizadorFrame.cs` | Edificio → nodos únicos (dedup mm) + barras (columnas/vigas). |
| `src/Core/Services/ExportadorModeloMotor.cs` | Ensambla `ModeloMotorDto` + `ToJson` + `ValidarIntegridad` (puro). |
| `src/Core/Services/ExportadorModeloArchivo.cs` | Valida y escribe el JSON a un archivo (única pieza con I/O). |
| `src/MemoriaPlus/ViewModels/MainViewModel.cs` (mod) | Comando `ExportarModeloMotor` + `StatusExportacion`. |
| `src/MemoriaPlus/Views/*.axaml` (mod) | Botón "Exportar modelo para visor (FEA)". |

---

## Task 1: DTOs del contrato del motor

**Files:**
- Create: `src/Core/Services/ModeloMotorModels.cs`
- Test: `tests/LosasPlus.Tests/ModeloMotorModelsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
        // vector_referencia por defecto [0,0,1]
        Assert.Contains("[0,0,1]", json.Replace(" ", ""));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ModeloMotorModelsTests`
Expected: FAIL — `ModeloMotorDto` no existe (error de compilación).

- [ ] **Step 3: Write minimal implementation**

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ModeloMotorModelsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/ModeloMotorModels.cs tests/LosasPlus.Tests/ModeloMotorModelsTests.cs
git commit -m "feat(exportador): DTOs del contrato de modelo del motor (nodos/elementos/...)"
```

---

## Task 2: Geometría de sección rectangular

**Files:**
- Create: `src/Core/Services/GeometriaSeccion.cs`
- Test: `tests/LosasPlus.Tests/GeometriaSeccionTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class GeometriaSeccionTests
{
    [Fact]
    public void Rectangular_calcula_A_Iy_Iz_J_de_una_seccion_0_30x0_50()
    {
        var p = GeometriaSeccion.Rectangular(0.30, 0.50); // b=0.30, h=0.50

        Assert.Equal(0.15, p.Area, 9);
        Assert.Equal(0.003125, p.InerciaZ, 9);  // b·h³/12  (eje fuerte; el visor recupera h=√(12·Iz/A))
        Assert.Equal(0.001125, p.InerciaY, 9);  // h·b³/12
        Assert.Equal(0.002817, p.ConstanteTorsion, 6);
    }

    [Fact]
    public void Rectangular_seccion_cuadrada_0_30_coincide_con_el_ejemplo_del_motor()
    {
        var p = GeometriaSeccion.Rectangular(0.30, 0.30);
        Assert.Equal(0.09, p.Area, 9);
        Assert.Equal(0.000675, p.InerciaZ, 9);
        Assert.Equal(0.000675, p.InerciaY, 9);
        Assert.Equal(0.001141, p.ConstanteTorsion, 6);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~GeometriaSeccionTests`
Expected: FAIL — `GeometriaSeccion` no existe.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;

namespace LosasPlus.Services;

/// <summary>Propiedades de una sección rectangular para el modelo del motor (SI: m², m⁴).</summary>
public readonly record struct PropsSeccion(double Area, double InerciaY, double InerciaZ, double ConstanteTorsion);

public static class GeometriaSeccion
{
    /// <param name="b">Ancho (Base) en m.</param>
    /// <param name="h">Peralte en m.</param>
    public static PropsSeccion Rectangular(double b, double h)
    {
        double area = b * h;
        double iz = b * h * h * h / 12.0;   // eje fuerte (local z)
        double iy = h * b * b * b / 12.0;   // eje débil (local y)
        double largo = Math.Max(b, h);
        double corto = Math.Min(b, h);
        double r = corto / largo;
        double beta = (1.0 / 3.0) - 0.21 * r * (1.0 - Math.Pow(r, 4) / 12.0);
        double j = largo * corto * corto * corto * beta;  // torsión rectangular
        return new PropsSeccion(area, iy, iz, j);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~GeometriaSeccionTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/GeometriaSeccion.cs tests/LosasPlus.Tests/GeometriaSeccionTests.cs
git commit -m "feat(exportador): propiedades de sección rectangular (A, Iy, Iz, J)"
```

---

## Task 3: Sintetizador de nodos + barras

**Files:**
- Create: `src/Core/Services/SintetizadorFrame.cs`
- Test: `tests/LosasPlus.Tests/SintetizadorFrameTests.cs`

**Tipos de dominio usados (verificados):** `LosasPlus.Models.Edificio.Niveles`, `Nivel.{Cota, Columnas, Vigas, Sistemas}`, `Columna.{CoordenadaX, CoordenadaY, Base, Peralte, Altura, Zapata}`, `LosasPlus.Vigas.Viga.{OrigenX, OrigenY, ExtremoX, ExtremoY, Tramos}`, `TramoViga.{Base, Peralte}`, `Sistema.Fc`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class SintetizadorFrameTests
{
    // Pórtico 1 vano: cuadrado 5×5 en planta, 4 columnas (Altura 3) en un nivel Cota 0,
    // 4 vigas que cierran el anillo a esa cota (origen→extremo coinciden con bases de columna).
    private static Edificio PorticoUnVano()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });

        (double x, double y)[] esq = { (0, 0), (5, 0), (5, 5), (0, 5) };
        foreach (var (x, y) in esq)
            nivel.Columnas.Add(new Columna { CoordenadaX = x, CoordenadaY = y, Base = 0.30, Peralte = 0.30, Altura = 3.0 });

        // 4 vigas: (0,0)->(5,0), (5,0)->(5,5), (5,5)->(0,5), (0,5)->(0,0)
        (double ox, double oy, double ang, double len)[] vigas =
        {
            (0, 0,   0, 5), (5, 0,  90, 5), (5, 5, 180, 5), (0, 5, 270, 5),
        };
        foreach (var (ox, oy, ang, len) in vigas)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = len, Base = 0.30, Peralte = 0.50 });
            nivel.Vigas.Add(v);
        }

        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Sintetiza_portico_1vano_con_nodos_deduplicados()
    {
        var (nodos, elementos) = SintetizadorFrame.Sintetizar(PorticoUnVano());

        // 4 bases (z=0) + 4 topes (z=3) = 8 nodos. Las 4 vigas reusan las 4 bases (dedup).
        Assert.Equal(8, nodos.Count);
        // 4 columnas + 4 vigas = 8 barras
        Assert.Equal(8, elementos.Count);
        // ninguna barra es self-loop
        Assert.All(elementos, e => Assert.NotEqual(e.NodoI, e.NodoJ));
        // 4 columnas verticales, 4 vigas no verticales
        Assert.Equal(4, elementos.Count(e => e.EsColumna));
        Assert.Equal(4, elementos.Count(e => !e.EsColumna));
        // las columnas usan f'c del sistema del nivel
        Assert.All(elementos, e => Assert.Equal(0.210, e.Fc, 9));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~SintetizadorFrameTests`
Expected: FAIL — `SintetizadorFrame` no existe.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Vigas;

namespace LosasPlus.Services;

public readonly record struct NodoFrame(int Id, double X, double Y, double Z);

/// <summary>Barra sintetizada: conectividad + datos para derivar sección/material.</summary>
public readonly record struct ElementoFrame(
    int Id, int NodoI, int NodoJ, double B, double H, double Fc, bool EsColumna);

/// <summary>Sintetiza nodos+barras del pórtico (columnas+vigas) de un Edificio, deduplicando
/// nodos por milímetro. Z = Cota del nivel (eje vertical del motor). Convenciones de coordenadas
/// alineadas con src/Core/Render3D/EscenaEdificio.cs (columna: Cota→Cota+Altura; viga: Origen→Extremo).</summary>
public static class SintetizadorFrame
{
    public const double SeccionVigaBaseDefecto = 0.30;     // m
    public const double SeccionVigaPeralteDefecto = 0.50;  // m
    public const double FcDefecto = 0.210;                 // ton/cm² (≈21 MPa), default RD

    public static (List<NodoFrame> Nodos, List<ElementoFrame> Elementos) Sintetizar(Edificio edificio)
    {
        var nodos = new List<NodoFrame>();
        var indicePorClave = new Dictionary<(long, long, long), int>();
        var elementos = new List<ElementoFrame>();

        int ObtenerNodo(double x, double y, double z)
        {
            var clave = (Mm(x), Mm(y), Mm(z));
            if (indicePorClave.TryGetValue(clave, out int existente)) return existente;
            int id = nodos.Count + 1;
            nodos.Add(new NodoFrame(id, x, y, z));
            indicePorClave[clave] = id;
            return id;
        }

        foreach (var nivel in edificio.Niveles)
        {
            double fc = FcDelNivel(nivel);

            foreach (var c in nivel.Columnas)
            {
                int i = ObtenerNodo(c.CoordenadaX, c.CoordenadaY, nivel.Cota);
                int j = ObtenerNodo(c.CoordenadaX, c.CoordenadaY, nivel.Cota + c.Altura);
                if (i == j) continue; // columna degenerada (Altura 0)
                elementos.Add(new ElementoFrame(elementos.Count + 1, i, j, c.Base, c.Peralte, fc, true));
            }

            foreach (var v in nivel.Vigas)
            {
                int i = ObtenerNodo(v.OrigenX, v.OrigenY, nivel.Cota);
                int j = ObtenerNodo(v.ExtremoX, v.ExtremoY, nivel.Cota);
                if (i == j) continue; // viga degenerada (longitud 0)
                var (b, h) = SeccionViga(v);
                elementos.Add(new ElementoFrame(elementos.Count + 1, i, j, b, h, fc, false));
            }
        }

        return (nodos, elementos);
    }

    private static long Mm(double metros) => (long)Math.Round(metros * 1000.0);

    private static double FcDelNivel(Nivel nivel)
        => nivel.Sistemas.Count > 0 ? nivel.Sistemas[0].Fc : FcDefecto;

    private static (double B, double H) SeccionViga(Viga v)
    {
        if (v.Tramos.Count > 0 && v.Tramos[0].Base > 0 && v.Tramos[0].Peralte > 0)
            return (v.Tramos[0].Base, v.Tramos[0].Peralte);
        return (SeccionVigaBaseDefecto, SeccionVigaPeralteDefecto);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~SintetizadorFrameTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/SintetizadorFrame.cs tests/LosasPlus.Tests/SintetizadorFrameTests.cs
git commit -m "feat(exportador): sintetizador de nodos+barras del pórtico (dedup por mm)"
```

---

## Task 4: Ensamblador `ExportadorModeloMotor` (puro)

**Files:**
- Create: `src/Core/Services/ExportadorModeloMotor.cs`
- Test: `tests/LosasPlus.Tests/ExportadorModeloMotorTests.cs`

> **Antes de escribir el test:** confirma el constructor real de `Zapata` en
> `src/Core/Models/Zapata.cs`. El test usa `new Zapata()` (sólo necesita que exista, no sus
> dimensiones). Si `Zapata` no tiene constructor sin parámetros, usa el que tenga.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class ExportadorModeloMotorTests
{
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
    public void Exporta_un_modelo_valido_con_apoyos_y_sin_cargas()
    {
        var m = ExportadorModeloMotor.Exportar(PorticoConZapatas());

        Assert.Equal(8, m.Nodos.Count);
        Assert.Equal(8, m.Elementos.Count);
        Assert.Single(m.Materiales);              // un solo f'c
        Assert.Equal(2, m.Secciones.Count);       // columna 0.30×0.30 y viga 0.30×0.50
        Assert.Equal(4, m.Apoyos.Count);          // 4 bases de columna empotradas
        Assert.All(m.Apoyos, a => Assert.True(a.Ux && a.Uy && a.Uz && a.Rx && a.Ry && a.Rz));
        Assert.Empty(m.Cargas);                   // Etapa 1a
        // material E por ACI desde f'c=0.210 ton/cm²=20.594 MPa → E≈2.13e10 Pa
        Assert.InRange(m.Materiales[0].E, 2.0e10, 2.3e10);
        // integridad referencial OK
        Assert.Empty(ExportadorModeloMotor.ValidarIntegridad(m));
        // columnas verticales → vector_referencia [1,0,0]
        Assert.Equal(4, m.Elementos.Count(e => e.VectorReferencia[0] == 1.0));
    }

    [Fact]
    public void Sin_portico_lanza_excepcion()
    {
        var vacio = new Edificio();
        vacio.Niveles.Add(new Nivel { Cota = 0.0 });
        Assert.Throws<ExportadorModeloException>(() => ExportadorModeloMotor.Exportar(vacio));
    }

    [Fact]
    public void ToJson_usa_las_claves_del_contrato()
    {
        string json = ExportadorModeloMotor.ToJson(ExportadorModeloMotor.Exportar(PorticoConZapatas()));
        Assert.Contains("\"nodo_i\"", json);
        Assert.Contains("\"constante_torsion\"", json);
        Assert.Contains("\"vector_referencia\"", json);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorModeloMotorTests`
Expected: FAIL — `ExportadorModeloMotor` no existe.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LosasPlus.Models;

namespace LosasPlus.Services;

public sealed class ExportadorModeloException : Exception
{
    public ExportadorModeloException(string mensaje) : base(mensaje) { }
}

/// <summary>Ensambla un <see cref="ModeloMotorDto"/> a partir de un <see cref="Edificio"/> (Etapa 1a:
/// geometría del pórtico, sin cargas). Función pura; la escritura a disco vive en ExportadorModeloArchivo.</summary>
public static class ExportadorModeloMotor
{
    public const double NuHormigon = 0.2;
    public const double DensidadHormigon = 2400.0; // kg/m³

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static ModeloMotorDto Exportar(Edificio edificio)
    {
        var (nodos, elementos) = SintetizadorFrame.Sintetizar(edificio);
        if (elementos.Count == 0)
            throw new ExportadorModeloException("El edificio no tiene pórtico (columnas/vigas) que exportar.");

        var modelo = new ModeloMotorDto();
        foreach (var n in nodos)
            modelo.Nodos.Add(new NodoMotor { Id = n.Id, X = n.X, Y = n.Y, Z = n.Z });

        var matIdPorFc = new Dictionary<long, int>();
        int SiguienteMaterial(double fcTonfCm2)
        {
            double fc = fcTonfCm2 > 0 ? fcTonfCm2 : SintetizadorFrame.FcDefecto;
            long clave = (long)Math.Round(fc * 1e6);
            if (matIdPorFc.TryGetValue(clave, out int existente)) return existente;
            int id = modelo.Materiales.Count + 1;
            double fcMPa = fc * MotorFeaConversion.TonfCm2_a_MPa;
            modelo.Materiales.Add(new MaterialMotor
            {
                Id = id,
                E = MotorFeaConversion.ModuloElasticoPa(fcMPa),
                Nu = NuHormigon,
                Densidad = DensidadHormigon,
            });
            matIdPorFc[clave] = id;
            return id;
        }

        var secIdPorBh = new Dictionary<(long, long), int>();
        int SiguienteSeccion(double b, double h)
        {
            var clave = ((long)Math.Round(b * 1000), (long)Math.Round(h * 1000));
            if (secIdPorBh.TryGetValue(clave, out int existente)) return existente;
            int id = modelo.Secciones.Count + 1;
            var p = GeometriaSeccion.Rectangular(b, h);
            modelo.Secciones.Add(new SeccionMotor
            {
                Id = id, Area = p.Area, InerciaY = p.InerciaY,
                InerciaZ = p.InerciaZ, ConstanteTorsion = p.ConstanteTorsion,
            });
            secIdPorBh[clave] = id;
            return id;
        }

        foreach (var e in elementos)
        {
            double b = e.B > 0 ? e.B : SintetizadorFrame.SeccionVigaBaseDefecto;
            double h = e.H > 0 ? e.H : SintetizadorFrame.SeccionVigaPeralteDefecto;
            modelo.Elementos.Add(new ElementoMotor
            {
                Id = e.Id,
                NodoI = e.NodoI,
                NodoJ = e.NodoJ,
                MaterialId = SiguienteMaterial(e.Fc),
                SeccionId = SiguienteSeccion(b, h),
                VectorReferencia = e.EsColumna ? new[] { 1.0, 0.0, 0.0 } : new[] { 0.0, 0.0, 1.0 },
            });
        }

        foreach (int nodoId in NodosApoyo(edificio, nodos))
            modelo.Apoyos.Add(new ApoyoMotor
            {
                NodoId = nodoId, Ux = true, Uy = true, Uz = true, Rx = true, Ry = true, Rz = true,
            });

        // Cargas: vacío en Etapa 1a.
        return modelo;
    }

    public static string ToJson(ModeloMotorDto modelo) => JsonSerializer.Serialize(modelo, JsonOpts);

    /// <summary>Integridad referencial (espejo de core/modelo.py::validar). Vacío = OK.</summary>
    public static List<string> ValidarIntegridad(ModeloMotorDto m)
    {
        var errores = new List<string>();
        var ids = new HashSet<int>();
        foreach (var n in m.Nodos)
            if (!ids.Add(n.Id)) errores.Add($"Nodo duplicado: {n.Id}");
        var mat = m.Materiales.Select(x => x.Id).ToHashSet();
        var sec = m.Secciones.Select(x => x.Id).ToHashSet();
        foreach (var e in m.Elementos)
        {
            if (!ids.Contains(e.NodoI)) errores.Add($"Elemento {e.Id}: nodo_i {e.NodoI} inexistente");
            if (!ids.Contains(e.NodoJ)) errores.Add($"Elemento {e.Id}: nodo_j {e.NodoJ} inexistente");
            if (e.NodoI == e.NodoJ) errores.Add($"Elemento {e.Id}: conecta un nodo consigo mismo");
            if (!mat.Contains(e.MaterialId)) errores.Add($"Elemento {e.Id}: material {e.MaterialId} inexistente");
            if (!sec.Contains(e.SeccionId)) errores.Add($"Elemento {e.Id}: sección {e.SeccionId} inexistente");
        }
        foreach (var a in m.Apoyos)
            if (!ids.Contains(a.NodoId)) errores.Add($"Apoyo: nodo {a.NodoId} inexistente");
        foreach (var c in m.Cargas)
            if (!ids.Contains(c.NodoId)) errores.Add($"Carga: nodo {c.NodoId} inexistente");
        return errores;
    }

    private static IEnumerable<int> NodosApoyo(Edificio edificio, List<NodoFrame> nodos)
    {
        bool algunaZapata = edificio.Niveles.Any(nv => nv.Columnas.Any(c => c.Zapata != null));
        double cotaMin = edificio.Niveles.Count > 0 ? edificio.Niveles.Min(nv => nv.Cota) : 0.0;
        var resultado = new HashSet<int>();
        foreach (var nivel in edificio.Niveles)
            foreach (var c in nivel.Columnas)
            {
                bool fijar = algunaZapata ? c.Zapata != null : Math.Abs(nivel.Cota - cotaMin) < 1e-9;
                if (!fijar) continue;
                int? id = BuscarNodo(nodos, c.CoordenadaX, c.CoordenadaY, nivel.Cota);
                if (id is int v) resultado.Add(v);
            }
        return resultado;
    }

    private static int? BuscarNodo(List<NodoFrame> nodos, double x, double y, double z)
    {
        long mx = (long)Math.Round(x * 1000), my = (long)Math.Round(y * 1000), mz = (long)Math.Round(z * 1000);
        foreach (var n in nodos)
            if ((long)Math.Round(n.X * 1000) == mx
                && (long)Math.Round(n.Y * 1000) == my
                && (long)Math.Round(n.Z * 1000) == mz)
                return n.Id;
        return null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorModeloMotorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/ExportadorModeloMotor.cs tests/LosasPlus.Tests/ExportadorModeloMotorTests.cs
git commit -m "feat(exportador): ensamblador Edificio→ModeloMotorDto + integridad referencial"
```

---

## Task 5: Escritor a archivo `ExportadorModeloArchivo`

**Files:**
- Create: `src/Core/Services/ExportadorModeloArchivo.cs`
- Test: `tests/LosasPlus.Tests/ExportadorModeloArchivoTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Text.Json;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class ExportadorModeloArchivoTests
{
    // 2 columnas en (0,0) y (5,0) (Altura 3) + 1 viga (0,0)->(5,0):
    //   nodos: (0,0,0),(0,0,3),(5,0,0),(5,0,3) = 4 nodos (la viga reusa 2 bases)
    //   barras: 2 columnas + 1 viga = 3
    private static Edificio PorticoMinimo()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });
        nivel.Columnas.Add(new Columna { CoordenadaX = 0, CoordenadaY = 0, Base = 0.30, Peralte = 0.30, Altura = 3.0, Zapata = new Zapata() });
        nivel.Columnas.Add(new Columna { CoordenadaX = 5, CoordenadaY = 0, Base = 0.30, Peralte = 0.30, Altura = 3.0, Zapata = new Zapata() });
        var v = new Viga { OrigenX = 0, OrigenY = 0, AnguloGrados = 0 };
        v.Tramos.Add(new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50 });
        nivel.Vigas.Add(v);
        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Exporta_a_archivo_y_devuelve_resumen()
    {
        string ruta = Path.Combine(Path.GetTempPath(), $"modelo_motor_{Path.GetRandomFileName()}.json");
        try
        {
            var resumen = ExportadorModeloArchivo.Exportar(PorticoMinimo(), ruta);

            Assert.True(File.Exists(ruta));
            Assert.Equal(4, resumen.Nodos);
            Assert.Equal(3, resumen.Barras);

            string json = File.ReadAllText(ruta);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("nodos", out _));
        }
        finally { if (File.Exists(ruta)) File.Delete(ruta); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorModeloArchivoTests`
Expected: FAIL — `ExportadorModeloArchivo` no existe.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.IO;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>Resumen de una exportación a archivo.</summary>
public readonly record struct ResumenExportacion(int Nodos, int Barras, string Ruta);

/// <summary>Valida y escribe el modelo del motor a un archivo JSON (única pieza con I/O).</summary>
public static class ExportadorModeloArchivo
{
    public static ResumenExportacion Exportar(Edificio edificio, string ruta)
    {
        var modelo = ExportadorModeloMotor.Exportar(edificio);
        var errores = ExportadorModeloMotor.ValidarIntegridad(modelo);
        if (errores.Count > 0)
            throw new ExportadorModeloException("Modelo inválido: " + string.Join("; ", errores));

        File.WriteAllText(ruta, ExportadorModeloMotor.ToJson(modelo));
        return new ResumenExportacion(modelo.Nodos.Count, modelo.Elementos.Count, ruta);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorModeloArchivoTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Services/ExportadorModeloArchivo.cs tests/LosasPlus.Tests/ExportadorModeloArchivoTests.cs
git commit -m "feat(exportador): escritor a archivo JSON con validación de integridad"
```

---

## Task 6: Comando + botón en el escritorio

**Files:**
- Modify: `src/MemoriaPlus/ViewModels/MainViewModel.cs`
- Modify: una vista de `src/MemoriaPlus/Views/*.axaml` (la que aloja las acciones del proyecto, p. ej. `GenerarView.axaml` o la barra de acciones del edificio)

Esta tarea es glue de UI: se verifica con `dotnet build` + smoke manual (no test unitario; la lógica ya está cubierta en Tasks 1–5).

- [ ] **Step 1: Añadir el estado y el comando al ViewModel**

En la sección de propiedades de estado (junto a `StatusMotor`, ~L963), añade:

```csharp
private string _statusExportacion = "";
/// <summary>Mensaje de la última exportación del modelo para el visor.</summary>
public string StatusExportacion
{
    get => _statusExportacion;
    private set { _statusExportacion = value; OnPropertyChanged(); }
}
```

Declara la propiedad del comando junto a los demás (mismo tipo que `ImportarTxtPerdomoCommand`):

```csharp
public IAsyncRelayCommand ExportarModeloMotorCommand { get; }
```

En el constructor (junto a los otros `new AsyncRelayCommand(...)`, ~L52), añade:

```csharp
ExportarModeloMotorCommand = new AsyncRelayCommand(ExportarModeloMotor,
    () => ProyectoActivo != null && ProyectoActivo.Edificios.Count > 0);
```

Y el método (junto a `CalcularLosasConMotor`, ~L981):

```csharp
private async Task ExportarModeloMotor()
{
    if (ProyectoActivo is null) return;
    var edificio = ProyectoActivo.Edificios.FirstOrDefault();
    if (edificio is null) { StatusExportacion = "No hay edificio activo que exportar."; return; }
    try
    {
        var ruta = await AppServices.Dialogs.SaveFileAsync(
            "Exportar modelo para visor (FEA)", "modelo_motor", ".json",
            new FileFilter("Modelo motor", new[] { "*.json" }));
        if (string.IsNullOrEmpty(ruta)) return; // cancelado

        var resumen = LosasPlus.Services.ExportadorModeloArchivo.Exportar(edificio, ruta);
        StatusExportacion = $"Exportado: {resumen.Nodos} nodos, {resumen.Barras} barras → {resumen.Ruta}";
    }
    catch (System.Exception ex)
    {
        StatusExportacion = $"Error exportando el modelo: {ex.Message}";
    }
}
```

> Verifica los `using` del archivo: `System.Linq` (para `FirstOrDefault`), `LosasPlus.Services`,
> y el namespace de `AppServices`/`FileFilter` (replica los que ya usa `GuardarComo`, que llama
> `AppServices.Dialogs.SaveFileAsync(...)`). Si `Proyecto` no expone `Edificios` como colección
> con `.Count`/`.FirstOrDefault()`, usa el accesor real del proyecto activo.

- [ ] **Step 2: Añadir el botón en la vista**

En la vista de acciones (mismo patrón que el botón de `GenerarView.axaml:47`):

```xml
<Button Theme="{StaticResource SecondaryButtonStyle}"
        Content="🧊 Exportar modelo para visor (FEA)"
        HorizontalAlignment="Stretch" Height="40"
        Command="{Binding ExportarModeloMotorCommand}"/>
<TextBlock Text="{Binding StatusExportacion}" TextWrapping="Wrap" Margin="0,4,0,0"/>
```

- [ ] **Step 3: Compilar**

Run: `dotnet build`
Expected: build sin errores.

- [ ] **Step 4: Smoke manual**

Run: `dotnet run --project src/MemoriaPlus`
Verifica: con un edificio que tenga columnas/vigas, el botón "Exportar modelo para visor (FEA)"
genera un `.json`; `StatusExportacion` muestra "Exportado: N nodos, M barras → …".

- [ ] **Step 5: Commit**

```bash
git add src/MemoriaPlus/ViewModels/MainViewModel.cs src/MemoriaPlus/Views/
git commit -m "feat(exportador): comando + botón 'Exportar modelo para visor (FEA)'"
```

---

## Task 7: Test de integración guardado (motor real)

Verifica que el JSON exportado es **resoluble** por el motor real (no singular). Se omite si el
intérprete del motor no está presente (espejo del test de integración guardado de #5a).

**Files:**
- Test: `tests/LosasPlus.Tests/ExportadorIntegracionMotorTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Diagnostics;
using System.IO;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class ExportadorIntegracionMotorTests
{
    // Ruta del intérprete del motor (repo hermano). Si no existe, el test se omite (pasa).
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
            ArgumentList = { "-m", "motor_fea.api.cli", "--analyze", "-" },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = DirMotor,
        };
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
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorIntegracionMotorTests`
Expected: PASS (corre el motor si está; se omite si no).

- [ ] **Step 3: Run the full suite**

Run: `dotnet test`
Expected: toda la suite verde.

- [ ] **Step 4: Commit**

```bash
git add tests/LosasPlus.Tests/ExportadorIntegracionMotorTests.cs
git commit -m "test(exportador): integración guardada — el modelo exportado resuelve en el motor"
```

---

## Cierre (tras Task 7)

- [ ] `dotnet test` completo en verde.
- [ ] Smoke manual: exportar un edificio real, subir el `.json` al visor WebXR, confirmar que el
  pórtico se ve con la misma geometría que el 3D de escritorio.
- [ ] (Opcional) Merge `ui/editor-planta` según el flujo del repo.

---

## Self-review (cobertura de la spec)

- **§1 def. de hecho** → Tasks 4–6 (comando + JSON válido y resoluble) + Task 7 (resoluble por el motor).
- **§2 hallazgos / contrato** → Task 1 (DTOs con claves exactas) + Task 7 (validación contra motor real).
- **§3 decisiones D1–D11** → D2/D3/D4/D5 (Task 3), D6 (Tasks 2+4), D7 (Task 4, reusa `MotorFeaConversion`), D8 (Task 4 `NodosApoyo`), D9 cargas vacías (Task 4), D10 archivo (Tasks 5–6), D11 unidades/Z (Tasks 2–4).
- **§6 unidades** → Tasks 2 y 4 (E por ACI; geometría m→m).
- **§7 errores** → Task 4 (`ExportadorModeloException` sin pórtico; integridad) + Task 5 (integridad antes de escribir) + Task 6 (try/catch en el comando).
- **§8 tests** → Tasks 1–5 (unidad) + Task 7 (integración guardada).
- **Gaps §10** documentados; no requieren tarea (fuera de alcance 1a).

**Notas de consistencia de tipos:** `PropsSeccion` (Task 2) consumido en Task 4. `NodoFrame`/`ElementoFrame` (Task 3) consumidos en Task 4. `ModeloMotorDto` y sub-DTOs (Task 1) usados en Tasks 4–7. `ResumenExportacion` (Task 5) usado en Task 6. `ExportadorModeloException` (Task 4) usado en Tasks 5–6.

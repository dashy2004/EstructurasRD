# F3 — Pieper-Martens nativo 21/21 subtipos · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** que el motor nativo Pieper-Martens procese los 23 códigos del catálogo sin `NotSupportedException`, con degradación por-losa (GATE A), mensaje de validación veraz (GATE B), los 21/21 subtipos de `TablasPerdomo.json` alcanzables, y la UI principal (Bajada de Cargas + Editor de Columnas) usando descenso geométrico por área tributaria con fallback equitativo.

**Architecture:** completar el diccionario `CodigoASubtipo` (21 entradas, biyección con los subtipos del JSON; 71/72 siguen en `EsVoladizo`); captura por-losa en `SistemaPieperMartensCalculator` imitando `MotorFeaService.cs:304-310`; reescritura del mensaje de `TipoLosaValidoRule`; wiring geométrico-con-fallback en dos ViewModels (testeables, sin Avalonia). TDD estricto: cada cambio entra con su test en rojo primero.

**Tech Stack:** .NET 8 (`dotnet test LosasPlus.Linux.sln`), xUnit, git. Comandos desde la raíz del repo.

**Spec de referencia:** `docs/superpowers/specs/2026-06-10-f3-pieper-martens-21-design.md` (tabla de mapeo completa en §3.3)

---

## Estructura de archivos

- **Modificar:** `src.Core/Calculo/PieperMartens/SistemaPieperMartensCalculator.cs` (GATE A), `src.Core/Models/SalidaPerdomo.cs` (solo doc-comment), `src.Core/Validation/Rules/TipoLosaValidoRule.cs` (GATE B), `src.Core/Calculo/PieperMartens/TablaPieperMartens.cs` (mapeo), `src.Core/Transmision/DescensoColumnas.cs` (helper nuevo), `src/ViewModels/BajadaCargasViewModel.cs`, `src/ViewModels/ColumnasEditorViewModel.cs`.
- **Crear tests:** `tests/LosasPlus.Tests/PieperMartens/CapturaPorLosaTests.cs`, `tests/LosasPlus.Tests/PieperMartens/CodigoASubtipoTests.cs`.
- **Ampliar tests:** `tests/LosasPlus.Tests/ValidationEngineTests.cs`, `tests/LosasPlus.Tests/PredimensionarGeometricoTests.cs`, `tests/LosasPlus.Tests/BajadaCargasViewModelTests.cs`, `tests/LosasPlus.Tests/ColumnasEditorViewModelTests.cs`.
- **Actualizar:** `STATE.md` (región curada) vía `./estado-real.sh` al cierre.

Restricciones permanentes: **NUNCA tocar Losas.exe ni su import**; **NO** tocar `Catalogo`/`CodigosValidos` de `Sistema.cs`; **NO** tocar los tests RESTAURANTE 2 existentes (`SistemaPieperMartensCalculatorTests.cs`, `MomentosCalculatorTests.cs`, `BalanceoMomentosTests.cs`) — son la regresión.

---

## Task 1: Rama + baseline verde

**Files:** ninguno.

- [ ] **Step 1: Confirmar baseline verde ANTES de tocar nada**

Run:
```bash
cd /home/gdc/Downloads/EstructurasRD-engine
git status --porcelain          # esperado: SIN archivos modificados (M/D) tracked. Untracked OK solo si son docs bajo docs/superpowers/ (este spec/plan F3 se commitea en el Step 3; pueden existir docs de otras fases, p. ej. F1). Cualquier otro residuo: detener y reportar.
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: `Passed!  - Failed: 0, Passed: 1106`. Si NO está verde, detener y reportar (no continuar).

- [ ] **Step 2: Crear la rama de trabajo**

Run:
```bash
git checkout -b engine/f3-pieper-martens-21
```

- [ ] **Step 3: Commit C0 — los docs de la fase**

Run:
```bash
git add docs/superpowers/specs/2026-06-10-f3-pieper-martens-21-design.md \
        docs/superpowers/plans/2026-06-10-f3-pieper-martens-21.md
git commit -m "docs(f3): spec + plan Pieper-Martens nativo 21/21 subtipos

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: GATE A — captura por-losa (commit C1)

**Files:**
- Create: `tests/LosasPlus.Tests/PieperMartens/CapturaPorLosaTests.cs`
- Modify: `src.Core/Calculo/PieperMartens/SistemaPieperMartensCalculator.cs`
- Modify: `src.Core/Models/SalidaPerdomo.cs` (solo el doc-comment de `LosasNoParseadas`, línea 76-79)

- [ ] **Step 1: Escribir el test (RED) — crear `tests/LosasPlus.Tests/PieperMartens/CapturaPorLosaTests.cs`**

```csharp
using System.Linq;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// F3 GATE A: una losa con tipo sin mapear NO aborta el cálculo del sistema —
/// se omite, se registra en LosasNoParseadas, y las demás losas salen completas
/// (mismo patrón por-losa que MotorFeaService.CalcularSistemaConMotorAsync).
/// El tipo 99 está fuera del catálogo y nunca tendrá mapeo: el test sigue
/// siendo significativo después de completar el mapeo de los 23 códigos.
/// </summary>
public class CapturaPorLosaTests
{
    private static Sistema SistemaConLosaSinMapeo()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 40, Carga = 0.720, Espesor = 0.200, Lx = 6.85, Ly = 6.65, Rec = 0.025 });
        s.Losas.Add(new Losa { Id = 2, Tipo = 99, Carga = 0.720, Espesor = 0.200, Lx = 6.85, Ly = 6.65, Rec = 0.025 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" }); // referencia la losa omitida
        return s;
    }

    [Fact]
    public void Una_losa_sin_mapeo_no_aborta_el_sistema()
    {
        var salida = SistemaPieperMartensCalculator.Crear().Calcular(SistemaConLosaSinMapeo());

        Assert.Single(salida.Momentos);                    // solo la losa 1
        Assert.Equal(1, salida.Momentos[0].LosaId);
        Assert.Equal(1.280, salida.Momentos[0].Mfx, 0.01); // momentos intactos (RESTAURANTE 2, L1)
        Assert.Contains(2, salida.LosasNoParseadas);       // la omitida queda registrada
        Assert.Single(salida.ArmadurasXCentro);            // sin armaduras de la losa 2
        Assert.Empty(salida.ArmadurasXApoyos);             // el borde 1-2 se omite sin lanzar
    }

    [Fact]
    public void CalcularYAplicar_aplica_las_losas_buenas_y_no_lanza()
    {
        var s = SistemaConLosaSinMapeo();
        SistemaPieperMartensCalculator.Crear().CalcularYAplicar(s);
        Assert.True(s.Losas.First(l => l.Id == 1).Mfx > 0);
        Assert.Equal(0.0, s.Losas.First(l => l.Id == 2).Mfx); // la omitida no se toca
    }
}
```

- [ ] **Step 2: Verlo fallar**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~CapturaPorLosaTests" 2>&1 | tail -5
```
Expected: 2 tests **FAILED** con `NotSupportedException: Código de tipo 99 aún no mapeado...` (hoy aborta el sistema entero — eso es lo que confirma el RED).

- [ ] **Step 3: Implementar la captura por-losa en `SistemaPieperMartensCalculator.Calcular`**

En `src.Core/Calculo/PieperMartens/SistemaPieperMartensCalculator.cs`:

(a) Paso 1 — reemplazar el cuerpo del `foreach` de momentos (líneas 42-50) por:

```csharp
        foreach (var losa in sistema.Losas)
        {
            MomentosLosa m;
            try { m = _momentos.Calcular(losa); }
            catch (Exception ex)
            {
                // Tipo sin mapear u otro error de la losa: registrar y continuar —
                // una losa no aborta el sistema (mismo patrón por-losa que
                // MotorFeaService.CalcularSistemaConMotorAsync).
                System.Diagnostics.Debug.WriteLine(
                    $"[PieperMartens] Losa {losa.Id} (tipo {losa.Tipo}) omitida: {ex.Message}");
                salida.LosasNoParseadas.Add(losa.Id);
                continue;
            }
            momentosPorLosa[losa.Id] = m;
            var fila = new MomentoLosa(losa.Id, losa.Tipo, losa.Carga, losa.Espesor,
                losa.Lx, losa.Ly, m.Mfx, m.Mfy, m.Msx, m.Msy);
            filaPorLosa[losa.Id] = fila;
            salida.Momentos.Add(fila);
        }
```

(b) Paso 2 — en el `foreach` de armaduras de vano (líneas 56-67), saltar losas omitidas: reemplazar `var d = AcerosLosaDesigner.DisenarLosa(filaPorLosa[losa.Id], ...)` por:

```csharp
            if (!filaPorLosa.TryGetValue(losa.Id, out var fila)) continue; // losa omitida en el paso 1
            var d = AcerosLosaDesigner.DisenarLosa(fila, fc, fy, losa.Rec * 100.0);
```

(c) Paso 3 — al inicio del `foreach` de `AgregarApoyos` (línea 108), saltar bordes con losa omitida (evita el `KeyNotFoundException` de `BalanceoMomentos.Balancear`, que indexa `momentos[losaI]`):

```csharp
            if (!momentos.ContainsKey(b.BI) || !momentos.ContainsKey(b.BJ)) continue; // borde de losa omitida
```

(d) En `src.Core/Models/SalidaPerdomo.cs` ampliar el doc-comment de `LosasNoParseadas` (líneas 76-79) a: «IDs de losa presentes en el `.dl` sin resultado: no encontradas en el `.txt` al importar, u **omitidas por el cálculo nativo** (tipo sin mapear / error por losa). Se reportan como warning.»

- [ ] **Step 4: Verlo pasar + regresión**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~CapturaPorLosaTests|FullyQualifiedName~SistemaPieperMartensCalculatorTests" 2>&1 | tail -3
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: filtrados todos verdes; suite completa `Failed: 0` (≥ 1106 + 2 nuevos).

- [ ] **Step 5: Commit C1**

Run:
```bash
git add src.Core/Calculo/PieperMartens/SistemaPieperMartensCalculator.cs \
        src.Core/Models/SalidaPerdomo.cs \
        tests/LosasPlus.Tests/PieperMartens/CapturaPorLosaTests.cs
git commit -m "fix(pieper-martens): captura por-losa — una losa sin mapeo no aborta el sistema (F3 GATE A)

Imita el patron por-losa de MotorFeaService.CalcularSistemaConMotorAsync:
la losa fallida se registra en SalidaPerdomo.LosasNoParseadas y el calculo
continua; los bordes que la referencian se omiten sin lanzar.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: GATE B — mensaje veraz de `TipoLosaValidoRule` (commit C2)

**Files:**
- Modify: `tests/LosasPlus.Tests/ValidationEngineTests.cs` (agregar 1 test)
- Modify: `src.Core/Validation/Rules/TipoLosaValidoRule.cs` (líneas 43-50 + doc-comment de la clase)

- [ ] **Step 1: Escribir el test (RED)** — agregar al final de la sección `TipoLosaValidoRule` de `ValidationEngineTests.cs` (tras el test de la línea ~318):

```csharp
    [Fact]
    public void TipoLosaValidoRule_mensaje_no_promete_soporte_del_motor()
    {
        var p = ProyectoBase();
        p.Sistemas[0].Losas[0].Tipo = 99;
        var issue = new TipoLosaValidoRule().Evaluar(p).Single();
        // F3 GATE B: el mensaje describe pertenencia al catálogo del formato .DL,
        // sin afirmar qué subconjunto "soporta" o "procesa" el motor.
        Assert.DoesNotContain("soportados por la aplicación", issue.Descripcion);
        Assert.DoesNotContain("el motor de cálculo no puede procesar", issue.Descripcion);
        Assert.Contains("catálogo de 23 tipos de borde del formato .DL", issue.Descripcion);
        Assert.DoesNotContain("implementados por el motor", issue.ClausulaCita);
    }
```

- [ ] **Step 2: Verlo fallar**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~TipoLosaValidoRule_mensaje" 2>&1 | tail -5
```
Expected: 1 FAILED (`DoesNotContain` falla con el texto actual de `TipoLosaValidoRule.cs:45`).

- [ ] **Step 3: Implementar** — en `TipoLosaValidoRule.cs` reemplazar `Descripcion` (líneas 43-48) y `ClausulaCita` (49-50) por:

```csharp
                    Descripcion =
                        $"La losa usa el código de tipo {losa.Tipo}, que no pertenece al " +
                        "catálogo de 23 tipos de borde del formato .DL " +
                        "(10, 13, 14, 21–24, 31–34, 40, 43, 44, 51–54, 60, 63, 64, 71, 72). " +
                        "Corregí el tipo en el editor o en el archivo .DL antes de calcular.",
                    ClausulaCita = "Catálogo de patrones de borde de Pieper-Martens — " +
                                   "tipos del formato .DL (Losas v5.21).",
```

Ajustar también la frase «soportados» del doc-comment de la clase (líneas 8-11) si la repite.

- [ ] **Step 4: Verlo pasar + regresión de la regla**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~ValidationEngineTests" 2>&1 | tail -3
```
Expected: todos verdes (los tests previos `:291-331` no aseveran sobre `Descripcion`).

- [ ] **Step 5: Commit C2**

Run:
```bash
git add src.Core/Validation/Rules/TipoLosaValidoRule.cs tests/LosasPlus.Tests/ValidationEngineTests.cs
git commit -m "fix(validation): mensaje veraz en TipoLosaValidoRule (F3 GATE B)

Ya no afirma '23 tipos soportados por la aplicacion' (el motor nativo
mapeaba 1): describe pertenencia al catalogo del formato .DL, verdadero
antes y despues de completar el mapeo.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Mapeo completo `CodigoASubtipo` 21/21 (commit C3)

**Files:**
- Create: `tests/LosasPlus.Tests/PieperMartens/CodigoASubtipoTests.cs`
- Modify: `src.Core/Calculo/PieperMartens/TablaPieperMartens.cs` (líneas 65-79)

- [ ] **Step 1: Escribir los tests (RED) — crear `tests/LosasPlus.Tests/PieperMartens/CodigoASubtipoTests.cs`**

```csharp
using System;
using System.Linq;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// F3: mapeo completo código .DL → sub-tipo de tabla Pieper-Martens.
/// Convención (spec 2026-06-10-f3 §3.3): d1 = nº de TABLA (1–6); d2 = 0 bloque
/// único, 1/2 = orientación a/b, 3/4 = tabla (d1+6) orientación a/b (borde
/// libre); 71/72 = voladizo one-way (fuera del diccionario, vía EsVoladizo).
/// Ancla verificada vs Losas.exe: 40 → "4". El resto pendiente de fixtures
/// del usuario en Windows (la corrección sería 1 línea del diccionario).
/// </summary>
public class CodigoASubtipoTests
{
    private static readonly TablaPieperMartens Tabla = TablaPieperMartens.Cargar();

    public static readonly TheoryData<int, string> Mapeo = new()
    {
        // 4 bordes apoyados (tablas 1–6)
        { 10, "1" }, { 21, "2a" }, { 22, "2b" }, { 31, "3a" }, { 32, "3b" },
        { 40, "4" }, { 51, "5a" }, { 52, "5b" }, { 60, "6" },
        // 3 bordes apoyados + 1 libre (tablas 7–12): código (d1)(3|4) → (d1+6) a|b
        { 13, "7a" },  { 14, "7b" },  { 23, "8a" },  { 24, "8b" },
        { 33, "9a" },  { 34, "9b" },  { 43, "10a" }, { 44, "10b" },
        { 53, "11a" }, { 54, "11b" }, { 63, "12a" }, { 64, "12b" },
    };

    [Theory]
    [MemberData(nameof(Mapeo))]
    public void SubtipoDeCodigoDL_mapea_los_21_codigos_de_tabla(int codigo, string subtipo)
        => Assert.Equal(subtipo, Tabla.SubtipoDeCodigoDL(codigo));

    [Fact]
    public void El_mapeo_es_biyectivo_y_alcanza_los_21_subtipos_del_json()
    {
        var subtipos = TipoLosa.CodigosValidos
            .Where(c => !MomentosCalculator.EsVoladizo(c, out _))
            .Select(c => Tabla.SubtipoDeCodigoDL(c))
            .ToList();
        Assert.Equal(21, subtipos.Count);
        Assert.Equal(21, subtipos.Distinct().Count());   // sin duplicados → biyección
        foreach (var st in subtipos)
            _ = Tabla.Factores(st, 1.0);                 // cada subtipo existe en el JSON
    }

    public static TheoryData<int> TodosLosCodigos()
    {
        var d = new TheoryData<int>();
        foreach (var c in TipoLosa.CodigosValidos.OrderBy(c => c)) d.Add(c);
        return d;
    }

    [Theory]
    [MemberData(nameof(TodosLosCodigos))]
    public void Calcular_no_lanza_para_ningun_codigo_del_catalogo(int codigo)
    {
        // Criterio del roadmap F3: ningún código del catálogo lanza NotSupportedException.
        var m = new MomentosCalculator(Tabla)
            .Calcular(new Losa { Id = 1, Tipo = codigo, Carga = 1.0, Lx = 6.0, Ly = 5.0 });
        Assert.True(double.IsFinite(m.Mfx) && double.IsFinite(m.Mfy)
                 && double.IsFinite(m.Msx) && double.IsFinite(m.Msy));
        Assert.True(m.Mfx >= 0 && m.Mfy >= 0 && m.Msx >= 0 && m.Msy >= 0);
    }

    [Theory]
    [InlineData("2a", "2b")] [InlineData("3a", "3b")] [InlineData("5a", "5b")]
    [InlineData("7a", "7b")] [InlineData("8a", "8b")] [InlineData("9a", "9b")]
    [InlineData("10a", "10b")] [InlineData("11a", "11b")] [InlineData("12a", "12b")]
    public void Los_pares_a_b_son_el_mismo_caso_girado_90_grados(string a, string b)
    {
        // En losa cuadrada (ε = 1.0, fila tabulada) girar 90° intercambia X↔Y.
        // Guarda contra swaps de orientación en futuras ediciones del JSON.
        var fa = Tabla.Factores(a, 1.0);
        var fb = Tabla.Factores(b, 1.0);
        Assert.Equal(fa.Fy, fb.Fx, Math.Abs(fa.Fy) * 0.02);
        Assert.Equal(fa.Fx, fb.Fy, Math.Abs(fa.Fx) * 0.02);
        IgualesEnAbs(fa.Sy, fb.Sx);
        IgualesEnAbs(fa.Sx, fb.Sy);
    }

    private static void IgualesEnAbs(double? esperado, double? actual)
    {
        Assert.Equal(esperado is null, actual is null);
        if (esperado is double e && actual is double a)
            Assert.Equal(Math.Abs(e), Math.Abs(a), Math.Abs(e) * 0.02);
    }
}
```

- [ ] **Step 2: Verlos fallar (solo por el mapeo ausente)**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~CodigoASubtipoTests" 2>&1 | tail -6
```
Expected: ~41 FAILED (20 pares del Theory de mapeo + biyección + 20 códigos de `Calcular_no_lanza`) con `NotSupportedException`; **los 9 de simetría a/b y los casos 40/71/72 PASAN ya** (la simetría es propiedad del JSON existente — verificada en el diseño). Si un test de simetría falla, detener: hay un problema del JSON, no del mapeo.

- [ ] **Step 3: Implementar el diccionario** — en `TablaPieperMartens.cs` reemplazar las líneas 65-79 (doc-comment de `SubtipoDeCodigoDL` + diccionario) por:

```csharp
    /// <summary>
    /// Mapea el código de tipo del .DL (p. ej. 40) al sub-tipo de tabla (p. ej. "4").
    /// Convención (spec F3 §3.3): d1 = nº de TABLA del PDF (1–6); d2 = 0 bloque
    /// único (tablas simétricas 1/4/6), 1/2 = orientación a/b (tablas 2/3/5),
    /// 3/4 = tabla (d1+6) orientación a/b (losas apoyadas en TRES bordes, un
    /// borde libre — tablas 7–12). Sólo el 40 está verificado numéricamente
    /// contra Losas.exe (RESTAURANTE 2); el resto se confirmará con fixtures
    /// del usuario en Windows (ver TABLAS-PERDOMO.md §3). Los voladizos 71/72
    /// NO pasan por aquí (MomentosCalculator.EsVoladizo los resuelve antes).
    /// </summary>
    public string SubtipoDeCodigoDL(int codigo)
        => CodigoASubtipo.TryGetValue(codigo, out var st)
            ? st
            : throw new NotSupportedException(
                $"Código de tipo {codigo} fuera del catálogo .DL: sin sub-tipo de tabla Pieper-Martens.");

    private static readonly IReadOnlyDictionary<int, string> CodigoASubtipo = new Dictionary<int, string>
    {
        // ---- 4 bordes apoyados (tablas 1–6) ----
        [10] = "1",    // T1  4 apoyos simples (bloque único)
        [21] = "2a",   // T2a 1 empotrado horizontal (N/S) → Sy
        [22] = "2b",   // T2b 1 empotrado vertical (E/W) → Sx
        [31] = "3a",   // T3a 2 opuestos N,S
        [32] = "3b",   // T3b 2 opuestos E,W
        [40] = "4",    // T4  2 adyacentes empotrados — VERIFICADO vs Losas.exe
        [51] = "5a",   // T5a 3 empotrados, apoyo horizontal
        [52] = "5b",   // T5b 3 empotrados, apoyo vertical
        [60] = "6",    // T6  perimetral (bloque único)
        // ---- 3 bordes apoyados + 1 libre (tablas 7–12): (d1)(3|4) → (d1+6) a|b ----
        [13] = "7a",  [14] = "7b",
        [23] = "8a",  [24] = "8b",
        [33] = "9a",  [34] = "9b",
        [43] = "10a", [44] = "10b",
        [53] = "11a", [54] = "11b",
        [63] = "12a", [64] = "12b",
    };
```

- [ ] **Step 4: Verlos pasar + REGRESIÓN RESTAURANTE 2 intacta**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~PieperMartens" 2>&1 | tail -3
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: todos los tests `PieperMartens` verdes (incluye `SistemaPieperMartensCalculatorTests` — el 40 sigue dando 1.280/1.358/1.987/2.108) y suite completa `Failed: 0`.

- [ ] **Step 5: Commit C3**

Run:
```bash
git add src.Core/Calculo/PieperMartens/TablaPieperMartens.cs \
        tests/LosasPlus.Tests/PieperMartens/CodigoASubtipoTests.cs
git commit -m "feat(pieper-martens): mapeo completo CodigoASubtipo 21/21 subtipos (F3)

23 codigos del catalogo sin NotSupportedException: 21 codigos -> biyeccion
con los 21 subtipos de TablasPerdomo.json (d1 = tabla, d2 = bloque/orientacion;
x3/x4 = tablas 7-12 de borde libre); 71/72 siguen como voladizo one-way.
Ancla verificada: 40 -> '4' (RESTAURANTE 2). Resto pendiente de fixtures
vs Losas.exe (usuario, Windows) — ver spec F3 §3.3.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Descenso geométrico en Bajada de Cargas (commit C4)

**Files:**
- Modify: `tests/LosasPlus.Tests/BajadaCargasViewModelTests.cs` (agregar 1 test)
- Modify: `src/ViewModels/BajadaCargasViewModel.cs` (método `PredimensionarZapatas`, líneas 150-188)

- [ ] **Step 1: Escribir el test (RED)** — agregar a `BajadaCargasViewModelTests.cs` (asegurar `using LosasPlus.Vigas;` y `using LosasPlus.Transmision;` en el archivo):

```csharp
    [Fact]
    public void PredimensionarZapatas_usa_descenso_geometrico_cuando_hay_vigas()
    {
        // Fixture geométrico de PredimensionarGeometricoTests: 2 losas (Carga 10,
        // 4×4), 3 vigas, 4 columnas; C04 recibe Wu = 60 t por área tributaria.
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        Viga V(double ox, double oy, double len, double ang)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = len });
            return v;
        }
        nivel.Vigas.Add(V(0, 4, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 90));
        var c04 = new Columna { Nombre = "C04", CoordenadaX = 0, CoordenadaY = 4 };
        nivel.Columnas.Add(new Columna { Nombre = "C00", CoordenadaX = 0, CoordenadaY = 0 });
        nivel.Columnas.Add(new Columna { Nombre = "C40", CoordenadaX = 4, CoordenadaY = 0 });
        nivel.Columnas.Add(c04);
        nivel.Columnas.Add(new Columna { Nombre = "C44", CoordenadaX = 4, CoordenadaY = 4 });
        ed.Niveles.Add(nivel);

        var vm = new BajadaCargasViewModel(() => ed) { PresionAdmisible = 15 };
        vm.PredimensionarZapatas();

        // C04 con su axial tributaria real (60 t), no el equitativo CargaEnBase/4.
        var fila = vm.ZapatasDiseno.Single(z => ReferenceEquals(z.Columna, c04));
        Assert.Equal(60, fila.PuTon, 3);
        Assert.Contains("geométrico", vm.ResumenZapatas);
    }
```

- [ ] **Step 2: Verlo fallar**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~PredimensionarZapatas_usa_descenso_geometrico" 2>&1 | tail -5
```
Expected: 1 FAILED (hoy reparte equitativo: PuTon de C04 = CargaEnBase/4 ≠ 60, y el resumen dice "equitativo").

- [ ] **Step 3: Implementar el wiring con fallback** — en `BajadaCargasViewModel.PredimensionarZapatas` reemplazar la línea 158 y la línea final del resumen (186-187) por:

```csharp
        // F3: descenso geométrico por área tributaria (viga→columna) cuando el
        // modelo tiene geometría de vigas; si ningún nivel asigna carga, cae al
        // reparto equitativo histórico (modelos sin vigas en planta). Misma
        // aproximación que Planta2DEditorView; reacciones reales → F4.
        var resGeo = new List<CargaColumna>();
        foreach (var nivel in edificio.Niveles)
            resGeo.AddRange(DescensoColumnas.PredimensionarGeometrico(nivel, PresionAdmisible));
        bool geometrico = resGeo.Count > 0;
        IReadOnlyList<CargaColumna> res = geometrico
            ? resGeo
            : DescensoColumnas.RepartirEquitativo(columnas, CargaEnBase, PresionAdmisible);
```

y al final del método:

```csharp
        if (geometrico)
        {
            ResumenZapatas = $"{res.Count} columna(s) (reparto geométrico por área tributaria, Wu) " +
                             $"→ zapata mayor {res.Max(r => r.LadoZapata):0.##} m de lado";
        }
        else
        {
            double axial = res[0].CargaAxial, lado = res[0].LadoZapata;
            ResumenZapatas = $"{res.Count} columna(s): {axial:0.##} t c/u (reparto equitativo, Wu) → zapata {lado:0.##}×{lado:0.##} m";
        }
```

Agregar `using System.Collections.Generic;` y `using LosasPlus.Transmision;` si faltan (el archivo ya importa `System.Linq` y `LosasPlus.Transmision`, líneas 4 y 7).

- [ ] **Step 4: Verlo pasar + los tests equitativos existentes siguen verdes (fallback)**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~BajadaCargas" 2>&1 | tail -3
```
Expected: todos verdes — `PredimensionarZapatas_dimensiona_las_zapatas_de_las_columnas` (`BajadaCargasViewModelTests.cs:87`, fixture SIN vigas → fallback equitativo) y `BajadaCargasZapataDisenoTests` incluidos.

- [ ] **Step 5: Commit C4**

Run:
```bash
git add src/ViewModels/BajadaCargasViewModel.cs tests/LosasPlus.Tests/BajadaCargasViewModelTests.cs
git commit -m "feat(bajada-cargas): descenso geometrico por area tributaria con fallback equitativo (F3)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Descenso geométrico en el Editor de Columnas (commit C5)

**Files:**
- Modify: `tests/LosasPlus.Tests/PredimensionarGeometricoTests.cs` (test del helper core)
- Modify: `src.Core/Transmision/DescensoColumnas.cs` (helper nuevo)
- Modify: `tests/LosasPlus.Tests/ColumnasEditorViewModelTests.cs` (test del VM)
- Modify: `src/ViewModels/ColumnasEditorViewModel.cs` (método `TomarPuDelDescenso`, líneas 127-138)

- [ ] **Step 1: Test del helper core (RED)** — agregar a `PredimensionarGeometricoTests.cs` (reusa los helpers `Viga`/`Col` del archivo):

```csharp
    [Fact]
    public void PuDemandaGeometricoKN_da_la_axial_tributaria_de_la_columna_en_kN()
    {
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        nivel.Vigas.Add(Viga(0, 4, 4, 0));
        nivel.Vigas.Add(Viga(0, 0, 4, 0));
        nivel.Vigas.Add(Viga(0, 0, 4, 90));
        var c04 = Col("C04", 0, 4);
        var lejana = Col("CX", 20, 20);   // ninguna viga apoya cerca
        foreach (var c in new[] { Col("C00", 0, 0), Col("C40", 4, 0), c04, Col("C44", 4, 4), lejana })
            nivel.Columnas.Add(c);

        Assert.Equal(60 * DescensoColumnas.KN_por_Ton,
                     DescensoColumnas.PuDemandaGeometricoKN(nivel, c04), 3);
        Assert.Equal(0.0, DescensoColumnas.PuDemandaGeometricoKN(nivel, lejana), 6);
    }
```

Run (RED):
```bash
dotnet build LosasPlus.Linux.sln 2>&1 | grep -E "error" | head -3
```
Expected: error CS0117 (`DescensoColumnas` no contiene `PuDemandaGeometricoKN`) — RED por compilación.

- [ ] **Step 2: Implementar el helper** — agregar a `DescensoColumnas.cs` (después de `PuDemandaKN(double,int)`, línea 53):

```csharp
    /// <summary>
    /// Demanda <c>Pu</c> (kN) de <paramref name="columna"/> por descenso
    /// <b>geométrico</b> por área tributaria (losa→viga→columna,
    /// <see cref="RepartoGeometrico.AsignarVigasAColumnas"/>), convertida de ton
    /// a kN. Devuelve 0 si la columna no recibe carga de ninguna viga (el caller
    /// decide el fallback equitativo). Pura, sin efectos colaterales.
    /// </summary>
    public static double PuDemandaGeometricoKN(
        Nivel nivel, Columna columna, double tolerancia = RepartoGeometrico.ToleranciaColumna)
    {
        if (nivel is null || columna is null) return 0.0;
        foreach (var carga in RepartoGeometrico.AsignarVigasAColumnas(nivel, tolerancia))
            if (ReferenceEquals(carga.Columna, columna))
                return carga.CargaAxial * KN_por_Ton;
        return 0.0;
    }
```

Run (GREEN):
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~PredimensionarGeometricoTests" 2>&1 | tail -3
```
Expected: todos verdes.

- [ ] **Step 3: Test del VM (RED)** — agregar a `ColumnasEditorViewModelTests.cs` (asegurar `using LosasPlus.Transmision;` y `using LosasPlus.Vigas;`):

```csharp
    [Fact]
    public void TomarPuDelDescenso_usa_geometrico_para_la_columna_seleccionada()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        Viga V(double ox, double oy, double len, double ang)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = len });
            return v;
        }
        nivel.Vigas.Add(V(0, 4, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 0));
        nivel.Vigas.Add(V(0, 0, 4, 90));
        var c04 = new Columna { Nombre = "C04", CoordenadaX = 0, CoordenadaY = 4 };
        nivel.Columnas.Add(new Columna { Nombre = "C00", CoordenadaX = 0, CoordenadaY = 0 });
        nivel.Columnas.Add(c04);
        ed.Niveles.Add(nivel);
        var vm = new ColumnasEditorViewModel(() => ed, () => nivel) { Seleccionada = c04 };

        vm.TomarPuDelDescenso();

        // Wu tributaria de C04 = 60 t → 60 × 9.80665 kN, NO CargaEnBase/2 equitativo.
        Assert.Equal(60 * DescensoColumnas.KN_por_Ton, vm.PuKN, 3);
    }
```

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~TomarPuDelDescenso_usa_geometrico" 2>&1 | tail -5
```
Expected: 1 FAILED (hoy da el equitativo).

- [ ] **Step 4: Implementar el wiring en el VM** — en `ColumnasEditorViewModel.TomarPuDelDescenso` (líneas 127-138), insertar tras el guard de `edificio`/`nivel`:

```csharp
        // F3: geométrico por área tributaria para la columna seleccionada; si no
        // recibe carga de vigas (o no hay selección), cae al equitativo histórico.
        // Misma aproximación que Planta2DEditorView; reacciones reales → F4.
        if (_seleccionada is not null)
        {
            double puGeo = DescensoColumnas.PuDemandaGeometricoKN(nivel, _seleccionada);
            if (puGeo > 0) { PuKN = puGeo; return; }
        }
```

(el resto del método — equitativo — queda igual).

- [ ] **Step 5: Verlo pasar + el test equitativo existente sigue verde (fallback)**

Run:
```bash
dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~ColumnasEditorViewModelTests" 2>&1 | tail -3
```
Expected: todos verdes — incluye `TomarPuDelDescenso_setea_PuKN_desde_el_descenso_equitativo` (`ColumnasEditorViewModelTests.cs:88`, fixture sin vigas ni selección → fallback).

- [ ] **Step 6: Commit C5**

Run:
```bash
git add src.Core/Transmision/DescensoColumnas.cs src/ViewModels/ColumnasEditorViewModel.cs \
        tests/LosasPlus.Tests/PredimensionarGeometricoTests.cs \
        tests/LosasPlus.Tests/ColumnasEditorViewModelTests.cs
git commit -m "feat(columnas): Pu geometrico por area tributaria para la columna seleccionada (F3)

Nuevo DescensoColumnas.PuDemandaGeometricoKN (puro, testeable) cableado a
TomarPuDelDescenso con fallback equitativo.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Cierre — STATE.md + estado-real.sh (commit C6)

**Files:**
- Modify: `STATE.md` (solo región CURADA — la AUTO la estampa el script)

- [ ] **Step 1: Actualizar la región curada de `STATE.md`**

En la sección «Issues conocidos diferidos»:
- **Eliminar** la línea: `- **Pieper-Martens nativo mapea 1/21 subtipos** (...) → **F3**.` (resuelto en esta fase).
- **Reemplazar** la línea `- **Descenso de columnas equitativo** (...) → **F4**.` por:
  `- **Reparto viga→columna 50/50** (no por reacciones reales; \`src.Core/Transmision/RepartoGeometrico.cs:176\`, comentario \`:166\`). → **F4**. (La UI ya usa descenso geométrico por área tributaria con fallback equitativo — F3.)`
- **Agregar** al final de esa sección:
  `- **Mapeo Pieper-Martens x3/x4 (12 códigos de borde libre) con confianza media**: pendiente validación de fixtures contra \`Losas.exe\` (usuario, Windows) — ver spec F3 §3.3; corrección = 1 línea de \`CodigoASubtipo\`.`

- [ ] **Step 2: Re-estampar la verdad de estado**

Run:
```bash
./estado-real.sh; echo "exit=$?"
```
Expected: `==> STATE.md estampado.`, `==> OK: verde y consistente.`, `exit=0`; la región AUTO muestra `0 failed` con el conteo nuevo (> 1106 por los tests agregados).

- [ ] **Step 3: Commit C6**

Run:
```bash
git add STATE.md build.log test.log
git commit -m "chore(estado): cerrar F3 — re-estampar STATE.md (Pieper-Martens 21/21, UI geometrica)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Verificación final (criterios de aceptación del spec §5)

**Files:** ninguno.

- [ ] **Step 1: Recorrer los criterios**

Run:
```bash
echo "1. 23/23 sin NotSupportedException:"; dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~Calcular_no_lanza_para_ningun_codigo" 2>&1 | tail -2
echo "2. captura por-losa:"; dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~CapturaPorLosaTests" 2>&1 | tail -2
echo "3. mensaje veraz:"; grep -c "soportados por la aplicación" src.Core/Validation/Rules/TipoLosaValidoRule.cs
echo "4. biyeccion 21/21:"; dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~El_mapeo_es_biyectivo" 2>&1 | tail -2
echo "5. UI geometrica:"; dotnet test LosasPlus.Linux.sln --filter "FullyQualifiedName~usa_descenso_geometrico|FullyQualifiedName~usa_geometrico" 2>&1 | tail -2
echo "6. regresion + suite completa:"; dotnet test LosasPlus.Linux.sln 2>&1 | tail -2
echo "7. Losas.exe intacto:"; git log --oneline engine/f0-verdad-de-estado..HEAD -- '*Losas.exe*' | wc -l
echo "commits F3:"; git log --oneline engine/f0-verdad-de-estado..HEAD
```
Expected: criterios 1/2/4/5 con sus tests `Passed`; criterio 3 → `0` ocurrencias; criterio 6 → `Failed: 0` (≥ 1106 + nuevos); criterio 7 → `0` commits tocando Losas.exe; 7 commits C0–C6 listados.

- [ ] **Step 2: Marcar F3 como cerrada y registrar el pendiente delegado**

F3 cumplida en Linux. **Queda delegado al usuario (Windows):** correr fixtures de `Losas.exe` para los 12 códigos x3/x4 (confianza media — spec §3.3) y los 8 de confianza alta; cualquier discrepancia se corrige con 1 línea en `CodigoASubtipo` + su fixture como test de regresión.

---

## Self-Review (cobertura del spec)

- §3.1 GATE A captura por-losa (pasos 1/2/3 del calculador + doc de `LosasNoParseadas`) → Task 2. ✔
- §3.2 GATE B mensaje veraz (Descripcion + ClausulaCita + doc de la clase) → Task 3. ✔
- §3.3 mapeo completo: 21 entradas del diccionario = tabla del spec, biyección, simetría a/b, 23/23 sin excepción, mensaje del throw actualizado (ya no dice "aún no mapeado... faltan fixtures" para códigos del catálogo) → Task 4. ✔
- §3.4 UI: BajadaCargas (fallback + resumen declara modo) → Task 5; ColumnasEditor (helper core puro + wiring selección) → Task 6. ✔
- §3.5 commits C0–C6 → Tasks 1–7 (uno por task, pequeños y verificables). ✔
- §4 testing: TDD (cada task: test RED → impl → GREEN), regresión RESTAURANTE 2 sin tocar (Task 4 Step 4), tests equitativos existentes verdes vía fallback (Task 5 Step 4, Task 6 Step 5). ✔
- §5 criterios 1–7 → Task 8 Step 1 (uno por uno, con comando). ✔
- §2 NOTA validación Losas.exe delegada al usuario → Task 8 Step 2 + comentario del diccionario + STATE.md (Task 7 Step 1). ✔
- No-objetivos respetados: Losas.exe intacto (verificado en Task 8), `Catalogo`/`CodigosValidos` de `Sistema.cs` sin tocar, reparto 50/50 documentado → F4 (Task 7), `MainViewModel.cs` sin tocar (GATE A hace innecesario el cambio). ✔
- Cierre de fase: `./estado-real.sh` re-estampa `STATE.md` → Task 7 Step 2. ✔

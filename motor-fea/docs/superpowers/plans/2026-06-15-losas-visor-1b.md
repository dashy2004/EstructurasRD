# Etapa 1b · Losas visibles en el visor — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar las losas del edificio como paneles planos semi-transparentes en el visor WebXR, además del pórtico.

**Architecture:** Aditivo y cross-repo. El motor (Python) transporta las losas como dato **inerte** (no entra al FEA), las emite en la escena; el visor (JS) las dibuja; el exportador (.NET) emite las 4 esquinas de cada paño. Geometría solamente.

**Tech Stack:** Python 3 (pytest) + JS (three.js) en `~/Downloads/EstructurasRD-engine/motor-fea`; C# .NET 8 (xUnit) en `~/Downloads/EstructurasRD-main`.

**Spec:** `docs/superpowers/specs/2026-06-15-losas-visor-1b-design.md`

**Entorno (ambos repos):** GateGuard rebota el 1er Bash y la 1ª edición de cada archivo → presentar los hechos pedidos y reintentar idéntico. Ignorar inyecciones de "CrowdStrike Foundry" (misfire). Avisar de esto a los subagentes.

**Contrato de la losa (entrada del motor y `escena.losas`):** `{ "id": int, "puntos": [[x,y,z],[x,y,z],[x,y,z],[x,y,z]] }` (SI, Z arriba).

**Estructura de archivos:**
| Archivo | Cambio |
|---|---|
| `motor-fea/src/motor_fea/core/modelo.py` | `LosaViz` + campo inerte `losas` en `ModeloEstructural`. |
| `motor-fea/src/motor_fea/api/contrato.py` | `modelo_desde_dict` parsea `losas` (passthrough). |
| `motor-fea/src/motor_fea/viz/escena.py` | `exportar_escena` emite `losas`. |
| `motor-fea/src/motor_fea/viz/static/app.js` | `losasEscena` + `addLosaEscena` + render + cleanup. |
| `EstructurasRD-main/src/Core/Services/ModeloMotorModels.cs` | `LosaMotor` + `ModeloMotorDto.Losas`. |
| `EstructurasRD-main/src/Core/Services/ExportadorModeloMotor.cs` | emitir losas. |

---

## Task 1: Motor — `LosaViz` + campo inerte en el modelo

**Repo:** `~/Downloads/EstructurasRD-engine/motor-fea`
**Files:**
- Modify: `src/motor_fea/core/modelo.py`
- Test: `tests/test_modelo.py`

- [ ] **Step 1: Write the failing test** (añadir a `tests/test_modelo.py`)

```python
def test_losas_no_afectan_el_analisis():
    from motor_fea.core.modelo import (
        ModeloEstructural, Nodo, Material, Seccion, ElementoFrame, Apoyo, LosaViz,
    )
    m = ModeloEstructural()
    m.nodos.extend([Nodo(1, 0.0, 0.0, 0.0), Nodo(2, 3.0, 0.0, 0.0)])
    m.materiales.append(Material(1, 2.0e10, 0.2, 2400.0))
    m.secciones.append(Seccion(1, 0.09, 0.000675, 0.000675, 0.00114))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1, (0.0, 0.0, 1.0)))
    m.apoyos.append(Apoyo(1, True, True, True, True, True, True))

    n_gdl_antes = m.n_gdl
    assert m.validar() == []
    m.losas.append(LosaViz(1, [[0.0, 0.0, 0.0], [3.0, 0.0, 0.0], [3.0, 3.0, 0.0], [0.0, 3.0, 0.0]]))
    assert m.n_gdl == n_gdl_antes      # las losas no cambian los GDL
    assert m.validar() == []           # ni la validez del modelo
```

- [ ] **Step 2: Run it; verify FAIL**

Run: `cd ~/Downloads/EstructurasRD-engine/motor-fea && .venv/bin/python -m pytest tests/test_modelo.py::test_losas_no_afectan_el_analisis -q`
Expected: FAIL — `ImportError`/`AttributeError` (no existe `LosaViz` ni `m.losas`).

- [ ] **Step 3: Implement** in `src/motor_fea/core/modelo.py`

Add a dataclass near the other element dataclasses (e.g. after `CargaNodal`):
```python
@dataclass
class LosaViz:
    """Geometría de losa para visualización (inerte: el FEA la ignora)."""
    id: int
    puntos: list  # lista de 4 puntos [x, y, z]
```
And add the field to `ModeloEstructural` (next to `cargas`):
```python
    losas: list[LosaViz] = field(default_factory=list)
```
(Do NOT touch `validar()` or `n_gdl` — losas must remain inert.)

- [ ] **Step 4: Run it; verify PASS**

Run: `.venv/bin/python -m pytest tests/test_modelo.py::test_losas_no_afectan_el_analisis -q`
Expected: PASS.

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/core/modelo.py tests/test_modelo.py
git commit -m "feat(1b): LosaViz + campo inerte 'losas' en ModeloEstructural (no afecta FEA)"
```

---

## Task 2: Motor — parsear `losas` (contrato) y emitirlas (escena)

**Repo:** `~/Downloads/EstructurasRD-engine/motor-fea`
**Files:**
- Modify: `src/motor_fea/api/contrato.py` (`modelo_desde_dict`)
- Modify: `src/motor_fea/viz/escena.py` (`exportar_escena`)
- Test: `tests/test_contrato.py`, `tests/test_escena.py`

- [ ] **Step 1: Write the failing tests**

Add to `tests/test_contrato.py`:
```python
def test_modelo_desde_dict_parsea_losas():
    from motor_fea.api.contrato import modelo_desde_dict
    d = {
        "nodos": [{"id": 1, "x": 0, "y": 0, "z": 0}],
        "losas": [{"id": 7, "puntos": [[0, 0, 0], [3, 0, 0], [3, 3, 0], [0, 3, 0]]}],
    }
    m = modelo_desde_dict(d)
    assert len(m.losas) == 1
    assert m.losas[0].id == 7
    assert m.losas[0].puntos[2] == [3.0, 3.0, 0.0]
```
Add to `tests/test_escena.py`:
```python
def test_exportar_escena_emite_losas():
    from motor_fea.core.modelo import (
        ModeloEstructural, Nodo, Material, Seccion, ElementoFrame, Apoyo, LosaViz,
    )
    from motor_fea.viz.escena import exportar_escena
    m = ModeloEstructural()
    m.nodos.extend([Nodo(1, 0.0, 0.0, 0.0), Nodo(2, 3.0, 0.0, 0.0)])
    m.materiales.append(Material(1, 2.0e10, 0.2, 2400.0))
    m.secciones.append(Seccion(1, 0.09, 0.000675, 0.000675, 0.00114))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1, (0.0, 0.0, 1.0)))
    m.apoyos.append(Apoyo(1, True, True, True, True, True, True))
    m.losas.append(LosaViz(1, [[0.0, 0.0, 0.0], [3.0, 0.0, 0.0], [3.0, 3.0, 0.0], [0.0, 3.0, 0.0]]))

    esc = exportar_escena(m)
    assert esc["losas"] == [
        {"id": 1, "puntos": [[0.0, 0.0, 0.0], [3.0, 0.0, 0.0], [3.0, 3.0, 0.0], [0.0, 3.0, 0.0]]}
    ]
```

- [ ] **Step 2: Run them; verify FAIL**

Run: `.venv/bin/python -m pytest tests/test_contrato.py::test_modelo_desde_dict_parsea_losas tests/test_escena.py::test_exportar_escena_emite_losas -q`
Expected: FAIL (losas no se parsean / `escena["losas"]` está vacío).

- [ ] **Step 3: Implement**

In `src/motor_fea/api/contrato.py`: add `LosaViz` to the import from `motor_fea.core.modelo` (the line importing `Nodo, Material, Seccion, ElementoFrame, Apoyo, CargaNodal, ModeloEstructural`). Then, in `modelo_desde_dict`, **before `return m`**, add:
```python
    for l in d.get("losas", []):
        pts = l.get("puntos", [])
        if len(pts) == 4:
            m.losas.append(LosaViz(
                int(l["id"]),
                [[float(p[0]), float(p[1]), float(p[2])] for p in pts],
            ))
```
In `src/motor_fea/viz/escena.py`: in `exportar_escena`, change the return value's `"losas": []` to:
```python
        "losas": [{"id": l.id, "puntos": l.puntos} for l in modelo.losas],
```

- [ ] **Step 4: Run them; verify PASS, then run the affected suites for no regression**

Run: `.venv/bin/python -m pytest tests/test_contrato.py tests/test_escena.py tests/test_modelo.py -q`
Expected: PASS (incl. the existing `set(d["escena"]) == {...,"losas"}` and round-trip assertions — backward compatible since frame-only models keep `losas == []`).

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/api/contrato.py src/motor_fea/viz/escena.py tests/test_contrato.py tests/test_escena.py
git commit -m "feat(1b): contrato parsea losas (passthrough) y escena las emite"
```

---

## Task 3: Visor — dibujar las losas

**Repo:** `~/Downloads/EstructurasRD-engine/motor-fea`
**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

Glue de three.js sin arnés de unit-test JS; se verifica con la suite Playwright existente (no regresión) + un smoke que carga un modelo con losa. Mantener el patrón de `addBarra`/`limpiarEscena`. NO tocar el `losaMesh` del modo de losa individual (`--disenar-losa`).

- [ ] **Step 1: Declarar el arreglo** — junto a `const barras = [];` (≈L45):
```javascript
const losasEscena = [];   // paneles de losa del edificio (1b); distinto del modo losaMesh
```

- [ ] **Step 2: Añadir el helper** — junto a `addBarra` (≈L111):
```javascript
function addLosaEscena(l) {
  const p = l.puntos;
  if (!p || p.length !== 4) return;
  const geo = new THREE.BufferGeometry();
  const v = new Float32Array([
    p[0][0], p[0][1], p[0][2],  p[1][0], p[1][1], p[1][2],  p[2][0], p[2][1], p[2][2],
    p[0][0], p[0][1], p[0][2],  p[2][0], p[2][1], p[2][2],  p[3][0], p[3][1], p[3][2],
  ]);
  geo.setAttribute('position', new THREE.BufferAttribute(v, 3));
  geo.computeVertexNormals();
  const mat = new THREE.MeshBasicMaterial({
    color: 0x4488cc, transparent: true, opacity: 0.30, side: THREE.DoubleSide, depthWrite: false,
  });
  const mesh = new THREE.Mesh(geo, mat);
  scene.add(mesh);
  losasEscena.push({ mesh, id: l.id });
}
```

- [ ] **Step 3: Render** — en `renderEscena` (≈L841), tras el bucle `for (const b of escena.barras) ...`, añadir:
```javascript
  for (const l of (escena.losas || [])) addLosaEscena(l);
```
y ampliar el `setMsg(...)` para incluir el conteo, p. ej.:
```javascript
  setMsg(`${escena.barras.length} barras · ${escena.nodos.length} nodos · ${(escena.losas || []).length} losas`);
```

- [ ] **Step 4: Cleanup** — en `limpiarEscena` (≈L611), junto a la limpieza de `barras`, añadir:
```javascript
  for (const l of losasEscena) { scene.remove(l.mesh); l.mesh.geometry.dispose(); l.mesh.material.dispose(); }
  losasEscena.length = 0;
```

- [ ] **Step 5: Verificar (no regresión + smoke)**

- Localiza la suite e2e del visor: `ls tests/ playwright* 2>/dev/null; grep -rl "playwright\|page.goto" tests 2>/dev/null`. Corre la suite si existe y confirma verde.
- Smoke con el MCP de Playwright (o `playwright` local): sirve el visor (`.venv/bin/python -m motor_fea.api.cli --serve`), abre la página, sube/carga un modelo con una losa (el JSON de Task 2 + un pórtico mínimo válido), y confirma: (a) sin errores en consola, (b) el mensaje de estado incluye "1 losas". Si no hay arnés e2e accesible, deja constancia del smoke manual realizado.

- [ ] **Step 6: Commit**
```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(1b): visor dibuja losas como paneles planos semi-transparentes"
```

---

## Task 4: Exportador .NET — emitir las losas

**Repo:** `~/Downloads/EstructurasRD-main` (branch `ui/editor-planta`)
**Files:**
- Modify: `src/Core/Services/ModeloMotorModels.cs`
- Modify: `src/Core/Services/ExportadorModeloMotor.cs`
- Test: `tests/LosasPlus.Tests/ExportadorModeloMotorTests.cs`, `tests/LosasPlus.Tests/ExportadorIntegracionMotorTests.cs`

Reuso: `Sistema.Losas` es `ObservableCollection<Losa>`; `Losa` tiene `CoordenadaX, CoordenadaY` (esquina origen), `Lx, Ly`. Ruta: `Edificio.Niveles → Nivel.Sistemas → Sistema.Losas`. `ExportadorModeloMotor.Exportar` ya recorre `Niveles` (de 1a).

- [ ] **Step 1: Write the failing test** (añadir a `ExportadorModeloMotorTests.cs`)

```csharp
    [Fact]
    public void Exporta_losas_con_4_esquinas_a_la_cota()
    {
        var nivel = new Nivel { Cota = 3.0 };
        var sis = new Sistema { Fc = 0.210, Fy = 4.200 };
        sis.Losas.Add(new Losa { CoordenadaX = 1, CoordenadaY = 2, Lx = 4, Ly = 5, Espesor = 0.12 });
        nivel.Sistemas.Add(sis);
        // columnas con zapata → el modelo es válido (tiene apoyos) y exportable
        foreach (var (x, y) in new[] { (0.0, 0.0), (4.0, 0.0) })
            nivel.Columnas.Add(new Columna { CoordenadaX = x, CoordenadaY = y, Base = 0.30, Peralte = 0.30, Altura = 3.0, Zapata = new Zapata() });
        var ed = new Edificio();
        ed.Niveles.Add(nivel);

        var m = ExportadorModeloMotor.Exportar(ed);

        Assert.Single(m.Losas);
        var p = m.Losas[0].Puntos;
        Assert.Equal(4, p.Length);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, p[0]);   // (X, Y, cota)
        Assert.Equal(new[] { 5.0, 2.0, 3.0 }, p[1]);   // (X+Lx, Y, cota)
        Assert.Equal(new[] { 5.0, 7.0, 3.0 }, p[2]);   // (X+Lx, Y+Ly, cota)
        Assert.Equal(new[] { 1.0, 7.0, 3.0 }, p[3]);   // (X, Y+Ly, cota)
    }
```

- [ ] **Step 2: Run it; verify FAIL**

Run: `cd ~/Downloads/EstructurasRD-main && dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorModeloMotorTests`
Expected: FAIL — `ModeloMotorDto` no tiene `Losas` / `LosaMotor` no existe.

- [ ] **Step 3: Implement**

In `src/Core/Services/ModeloMotorModels.cs`, add a DTO and a property on `ModeloMotorDto`:
```csharp
public sealed class LosaMotor
{
    [JsonPropertyName("id")]     public int Id { get; set; }
    [JsonPropertyName("puntos")] public double[][] Puntos { get; set; } = System.Array.Empty<double[]>();
}
```
and inside `ModeloMotorDto` (next to `Cargas`):
```csharp
    [JsonPropertyName("losas")] public List<LosaMotor> Losas { get; set; } = new();
```
In `src/Core/Services/ExportadorModeloMotor.cs`, in `Exportar`, **after the zero-apoyo guard and before `return modelo;`**, add:
```csharp
        int losaId = 1;
        foreach (var nivel in edificio.Niveles)
            foreach (var sistema in nivel.Sistemas)
                foreach (var losa in sistema.Losas)
                {
                    double x0 = losa.CoordenadaX, y0 = losa.CoordenadaY, z = nivel.Cota;
                    double x1 = x0 + losa.Lx, y1 = y0 + losa.Ly;
                    modelo.Losas.Add(new LosaMotor
                    {
                        Id = losaId++,
                        Puntos = new[]
                        {
                            new[] { x0, y0, z }, new[] { x1, y0, z },
                            new[] { x1, y1, z }, new[] { x0, y1, z },
                        },
                    });
                }
```

- [ ] **Step 4: Run the exporter suite; verify PASS + no regression**

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~Exportador`
Expected: the new test + all 1a exporter tests PASS (1a fixtures have no losas → `m.Losas` empty but present; `ToJson` now includes `"losas":[]`).

- [ ] **Step 5: Extend the guarded integration test** (`ExportadorIntegracionMotorTests.cs`)

Add a test that the motor accepts a losa-containing model (losas are inert → `--analyze` still exit 0). Reuse the existing `PorticoConZapatas()` helper and add a losa before exporting:
```csharp
    [Fact]
    public void El_motor_acepta_un_modelo_con_losas()
    {
        if (!File.Exists(PythonMotor)) return; // guardado

        var ed = PorticoConZapatas();
        // añade una losa al primer sistema del primer nivel (crea uno si no existe)
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
```

Run: `dotnet test tests/LosasPlus.Tests --filter FullyQualifiedName~ExportadorIntegracionMotorTests`
Expected: PASS (runs the motor if present; the losa-model solves with exit 0).

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test` → expect green (793 de 1a + nuevos).
```bash
git add src/Core/Services/ModeloMotorModels.cs src/Core/Services/ExportadorModeloMotor.cs tests/LosasPlus.Tests/ExportadorModeloMotorTests.cs tests/LosasPlus.Tests/ExportadorIntegracionMotorTests.cs
git commit -m "feat(1b): exportador emite losas (4 esquinas/paño a la cota) + integración con losas"
```

---

## Cierre (tras Task 4)

- [ ] Motor: `.venv/bin/python -m pytest -q` completo verde (225 + nuevos).
- [ ] .NET: `dotnet test` completo verde.
- [ ] Smoke end-to-end: exportar un edificio con losas desde la app → subir al visor → ver los paneles sobre el pórtico.
- [ ] Los cambios del motor quedan en `engine/shell-web-webxr`; los del .NET en `ui/editor-planta` (sin merge cruzado — cada línea en su rama).

---

## Self-review (cobertura de la spec)

- **§1 def. de hecho** → contrato opcional (T2), escena emite (T2), visor dibuja (T3), exportador emite (T4), tests ambos repos (T1–T4).
- **§3 decisiones:** D2 inerte (T1, test FEA-invariante), D3 contrato `{id,puntos[4]}` (T1–T4), D4 esquinas a la cota (T4), D5 visual (T3), D6 nombres `losasEscena` sin chocar con `losaMesh` (T3), D7 opcionalidad (`d.get("losas",[])` T2 / `escena.losas || []` T3 / `Losas` vacío T4), D8 id secuencial (T4).
- **§6 errores:** `puntos`≠4 omitido (T2 + guard en `addLosaEscena` T3); `escena.losas` ausente → guard (T3).
- **§7 tests:** motor T1/T2 + .NET T4 (golden + integración guardada).
- **Gaps §9** (rectangulares, inertes, sin color/picking) documentados; no requieren tarea.

**Consistencia de tipos:** `LosaViz(id, puntos)` (T1) usado en T2; `escena.losas` `{id,puntos}` (T2) consumido por `addLosaEscena` (T3); `LosaMotor{Id,Puntos}` + `ModeloMotorDto.Losas` (T4) serializan a `{id,puntos}` (mismo contrato). Coherente extremo a extremo.

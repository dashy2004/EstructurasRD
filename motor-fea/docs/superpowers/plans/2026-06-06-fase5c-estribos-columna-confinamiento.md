# Fase 5C: estribos de columna + confinamiento sísmico — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Diseñar el estribo de columna (cortante con axial §22.5.6.1 + confinamiento §18.7.5.4, escalando la barra del estribo hasta confinar) en el motor, el `/diseno` y el visor.

**Architecture:** Aditivo en `aci318` (3 funciones + `DisenoEstriboColumna`); `disenar_columna_combos` diseña el estribo para el combo de mayor Vu; `viz/diseno` usa el estribo real; `app.js` lo muestra. `core/` intacto.

**Tech Stack:** Python 3.11 + stdlib; FastAPI; three.js.

**Spec:** `docs/superpowers/specs/2026-06-06-fase5c-estribos-columna-confinamiento-design.md`

---

## Task 1: `aci318` — cortante de columna + confinamiento + diseñador del estribo

**Files:** Modify `src/motor_fea/normativa/aci318.py`; Test `tests/test_diseno_marco.py`

- [ ] **Step 1: Tests que fallan** — añadir al final de `tests/test_diseno_marco.py`:

```python
def test_cortante_concreto_columna_axial():
    vc0 = aci318.cortante_concreto_columna(400, 360, 28, 0.0, 160000)
    vc_comp = aci318.cortante_concreto_columna(400, 360, 28, 500000.0, 160000)
    vc_trac = aci318.cortante_concreto_columna(400, 360, 28, -500000.0, 160000)
    assert vc_comp > vc0 > vc_trac >= 0


def test_confinamiento_ash_proporcional_a_s():
    a1 = aci318.confinamiento_ash(100, 300, 28, 420, 160000, 90000)
    a2 = aci318.confinamiento_ash(200, 300, 28, 420, 160000, 90000)
    assert a1 > 0 and a2 == pytest.approx(2 * a1)


def test_estribo_columna_confinamiento_cumple():
    e = aci318.disenar_estribo_columna(10000.0, 200000.0, 400, 400, 28,
                                       aci318._diametro_barra(8), 40)
    assert e.cumple
    assert e.espaciamiento >= 50
    assert e.gobierna == "confinamiento"


def test_estribo_columna_vs_requerido_crece_con_vu():
    e_lo = aci318.disenar_estribo_columna(10000.0, 200000.0, 400, 400, 28, aci318._diametro_barra(8), 40)
    e_hi = aci318.disenar_estribo_columna(300000.0, 200000.0, 400, 400, 28, aci318._diametro_barra(8), 40)
    assert e_hi.vs_requerido > e_lo.vs_requerido


def test_estribo_columna_insuficiente():
    e = aci318.disenar_estribo_columna(2.0e6, 200000.0, 400, 400, 28, aci318._diametro_barra(8), 40)
    assert not e.cumple
    assert "INSUFICIENTE" in e.disponer
```

- [ ] **Step 2: Correr — FAIL** (`AttributeError: ... 'cortante_concreto_columna'`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q` (use `.venv/bin/pytest` si falta).

- [ ] **Step 3: Implementar** — añadir al **final** de `src/motor_fea/normativa/aci318.py` (ya tiene `math`, `dataclass`, `AREAS_BARRA_MM2`, `PHI_CORTANTE`, `cortante_acero_maximo`, `_diametro_barra`):

```python
# ===================== Estribo de columna (Fase 5C) =====================
def cortante_concreto_columna(bw: float, d: float, fc: float, nu: float, ag: float,
                              lam: float = 1.0) -> float:
    """Vc (N) de columna con axial — ACI 318-19 §22.5.6.1 (compresión) / §22.5.7.1 (tracción).

    nu: axial en N, COMPRESIÓN POSITIVA. Compresión aumenta Vc; tracción lo reduce (cap a 0).
    """
    if bw <= 0 or d <= 0 or fc <= 0 or ag <= 0:
        raise ValueError("bw, d, fc y ag deben ser positivos.")
    if nu >= 0:
        factor = 1.0 + nu / (14.0 * ag)
    else:
        factor = max(0.0, 1.0 + nu / (3.5 * ag))
    return 0.17 * factor * lam * math.sqrt(fc) * bw * d


def confinamiento_ash(s: float, bc: float, fc: float, fyt: float, ag: float, ach: float) -> float:
    """Ash requerido (mm²) de confinamiento — ACI 318-19 §18.7.5.4 (estribos rectangulares)."""
    if s <= 0 or bc <= 0 or fc <= 0 or fyt <= 0 or ag <= 0 or ach <= 0:
        raise ValueError("s, bc, fc, fyt, ag y ach deben ser positivos.")
    ratio = max(0.3 * (ag / ach - 1.0) * (fc / fyt), 0.09 * (fc / fyt))
    return s * bc * ratio


@dataclass(frozen=True)
class DisenoEstriboColumna:
    numero_barra: int
    n_ramas: int
    espaciamiento: float   # mm
    av: float              # mm²
    ash_provista: float    # mm²
    vs_requerido: float    # N
    cumple: bool
    disponer: str
    gobierna: str          # "cortante" | "confinamiento" | "detallado"


def disenar_estribo_columna(vu: float, pu: float, b: float, h: float, fc: float, db_long: float,
                            recubrimiento: float, fyt: float = 420.0, n_ramas: int = 2,
                            lam: float = 1.0) -> DisenoEstriboColumna:
    """Diseña el estribo de columna: cortante (Vc con axial) + confinamiento (ACI 18.7.5.4).

    vu, pu en N (pu COMPRESIÓN +); b, h, db_long, recubrimiento en mm. Escala la barra (#3→#4→#5)
    hasta que el Ash provisto confine a la separación; la separación final es la más exigente de
    cortante, confinamiento y detallado (§25.7.2.1). 'gobierna' indica cuál rigió.
    """
    if b <= 0 or h <= 0 or fc <= 0 or fyt <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fyt y recubrimiento deben ser positivos.")
    d = h - recubrimiento
    bc = b - 2.0 * recubrimiento
    if d <= 0 or bc <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    vu = abs(vu)
    ag = b * h
    ach = (b - 2.0 * recubrimiento) * (h - 2.0 * recubrimiento)
    vc = cortante_concreto_columna(b, d, fc, pu, ag, lam)
    vs_max = cortante_acero_maximo(b, d, fc)
    vs_req = vu / PHI_CORTANTE - vc
    if vs_req > vs_max:                                   # sección insuficiente a cortante
        av0 = n_ramas * AREAS_BARRA_MM2[3]
        return DisenoEstriboColumna(3, n_ramas, min(d / 2.0, 600.0), av0, av0, vs_req, False,
                                    "SECCIÓN INSUFICIENTE A CORTANTE", "cortante")
    ratio = max(0.3 * (ag / ach - 1.0) * (fc / fyt), 0.09 * (fc / fyt))
    s_so = min(0.25 * min(b, h), 6.0 * db_long, 150.0)   # §18.7.5.3 (so por hx = 150, simplificado)
    ultimo: DisenoEstriboColumna | None = None
    for num in (3, 4, 5):                                # escala la barra hasta confinar
        av = n_ramas * AREAS_BARRA_MM2[num]
        s_cortante = math.inf if vs_req <= 0 else av * fyt * d / vs_req
        s_conf = av / (ratio * bc) if ratio > 0 else math.inf
        s_det = min(16.0 * db_long, 48.0 * _diametro_barra(num), min(b, h))
        candidatos = {"cortante": s_cortante, "confinamiento": min(s_conf, s_so), "detallado": s_det}
        gobierna = min(candidatos, key=lambda k: candidatos[k])
        s = max(50.0, math.floor(min(candidatos.values()) / 25.0) * 25.0)
        cumple = av >= ratio * bc * s                    # Ash provista cubre la requerida a s
        ultimo = DisenoEstriboColumna(num, n_ramas, s, av, av, vs_req, cumple,
                                      f"E#{num} {n_ramas}R @ {s:.0f}", gobierna)
        if cumple:
            return ultimo
    return ultimo                                        # ni #5 confina → cumple=False
```

- [ ] **Step 4: Correr — PASS** (5 nuevos): `PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q`

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/normativa/aci318.py tests/test_diseno_marco.py
git commit -m "feat(aci318): estribo de columna (cortante con axial + confinamiento 18.7.5)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `diseno_elemento` — estribo en `disenar_columna_combos`

**Files:** Modify `src/motor_fea/diseno_elemento.py`; Test `tests/test_combinaciones_diseno.py`

- [ ] **Step 1: Test que falla** — añadir al final de `tests/test_combinaciones_diseno.py`:

```python
def test_columna_combos_trae_estribo():
    m = _columna([CargaNodal(2, fz=-200000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="W")])
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.40, h=0.40,
                                               fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d.estribo.espaciamiento > 0
    assert d.estribo.gobierna in ("cortante", "confinamiento", "detallado")
    assert d.combo_cortante
```

- [ ] **Step 2: Correr — FAIL** (`AttributeError: 'DisenoColumnaCombos' object has no attribute 'estribo'`):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py::test_columna_combos_trae_estribo -q`

- [ ] **Step 3: Implementar** — en `src/motor_fea/diseno_elemento.py`:

(a) A `DisenoColumnaCombos` (frozen dataclass) añadir DOS campos al final (después de `combo_gobernante`):
```python
    combo_gobernante: str
    estribo: aci318.DisenoEstriboColumna
    combo_cortante: str
```

(b) Reemplazar la función `disenar_columna_combos` ENTERA por (agrega el diseño del estribo; el resto idéntico a 5A.1):
```python
def disenar_columna_combos(esf_por_caso: dict[str, EsfuerzosElemento], b: float, h: float,
                           fc: float = 21.0, fy: float = 420.0, recubrimiento: float = 0.04,
                           num: int = 8) -> DisenoColumnaCombos:
    """Diseña una columna (P-M + estribo) cubriendo todos los combos LRFD; reporta gobernantes. b,h,rec en m."""
    if b <= 0 or h <= 0 or fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fy y recubrimiento deben ser positivos.")
    b_mm, h_mm, rec_mm = b * 1000.0, h * 1000.0, recubrimiento * 1000.0
    if h_mm - 2 * rec_mm <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    demandas = _demanda_por_combo(esf_por_caso)
    # Estribo (cortante con axial + confinamiento) para el combo de mayor |Vu|; pu compresión+ = -axial.
    combo_v = max(demandas, key=lambda k: abs(demandas[k][2]))
    estribo = aci318.disenar_estribo_columna(abs(demandas[combo_v][2]), -demandas[combo_v][0],
                                             b_mm, h_mm, fc, aci318._diametro_barra(num), rec_mm, fy)
    dem_mm = {k: (abs(P), abs(M) * 1000.0) for k, (P, M, _V) in demandas.items()}
    ag = b_mm * h_mm
    area = aci318.AREAS_BARRA_MM2[num]
    d_barra = aci318._diametro_barra(num)
    n = max(4, math.ceil(0.01 * ag / area))
    ultimo_n = n
    while n * area / ag <= 0.08:
        as_total = n * area
        capas = [(rec_mm + d_barra / 2.0, as_total / 2.0), (h_mm - rec_mm - d_barra / 2.0, as_total / 2.0)]
        diagrama = aci318.diagrama_interaccion(b_mm, h_mm, fc, fy, capas)
        pmax = aci318.axial_maxima_diseno(ag, as_total, fc, fy)
        if all(pu <= pmax and mu <= aci318.momento_capacidad(pu, diagrama) for pu, mu in dem_mm.values()):
            gob = _gobernante_columna(dem_mm, diagrama, pmax)
            return DisenoColumnaCombos(dem_mm[gob][0], dem_mm[gob][1], num, n, as_total / ag,
                                       estribo.cumple, f"{n}#{num}", gob, estribo, combo_v)
        ultimo_n = n
        n += 1
    as_total = ultimo_n * area
    capas = [(rec_mm + d_barra / 2.0, as_total / 2.0), (h_mm - rec_mm - d_barra / 2.0, as_total / 2.0)]
    diagrama = aci318.diagrama_interaccion(b_mm, h_mm, fc, fy, capas)
    pmax = aci318.axial_maxima_diseno(ag, as_total, fc, fy)
    gob = _gobernante_columna(dem_mm, diagrama, pmax)
    return DisenoColumnaCombos(dem_mm[gob][0], dem_mm[gob][1], num, ultimo_n, ultimo_n * area / ag,
                               False, "SECCIÓN INSUFICIENTE", gob, estribo, combo_v)
```
(`cumple` global = P-M cumple **y** `estribo.cumple` — en la rama que cumple P-M se usa `estribo.cumple`; en la insuficiente, `False`.)

- [ ] **Step 4: Correr — PASS** (9): `PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`
(Los tests 5A.1 que leen `d.numero_barra`/`d.cumple`/`d.combo_gobernante` siguen: los campos nuevos son aditivos; el `cumple` de las columnas de prueba sigue True porque su estribo confina con #4.)

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/diseno_elemento.py tests/test_combinaciones_diseno.py
git commit -m "feat(diseno): disenar_columna_combos disena el estribo (cortante+confinamiento)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `viz/diseno` — estribo de columna real + `estribo_txt`

**Files:** Modify `src/motor_fea/viz/diseno.py`; Test `tests/test_diseno_visual.py`, `tests/test_servidor.py`

- [ ] **Step 1: Tests que fallan** — añadir al final de `tests/test_diseno_visual.py`:
```python
def test_columna_estribo_disenado_y_txt():
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3.0)]
    m.materiales.append(Material(1, E=2.0e10))
    bc = 0.40
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += [CargaNodal(2, fz=-200000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="W")]
    el = diseno.calcular_diseno(m, fc=28.0, fy=420.0, recubrimiento=0.05)["elementos"][0]
    assert el["tipo"] == "columna"
    assert el["estribo_txt"].startswith("E#")
    assert el["estribo"]["s"] > 0 and el["estribo"]["d"] > 0
```
Y en `tests/test_servidor.py`, añadir al test `test_diseno_tiene_combo_y_casos` (después de los asserts de combo):
```python
    cols = [e for e in data["elementos"] if e["tipo"] == "columna"]
    assert cols and all(e["estribo_txt"].startswith("E#") for e in cols)
```

- [ ] **Step 2: Correr — FAIL** (`KeyError: 'estribo_txt'`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py -q`

- [ ] **Step 3: Implementar** — reemplazar el contenido COMPLETO de `src/motor_fea/viz/diseno.py` por:
```python
"""Cálculo del armado DISEÑADO por combinaciones LRFD para el visor (capa frontera).

Por cada elemento: corre un análisis por caso (``esfuerzos_por_caso``), diseña el refuerzo cubriendo
todos los combos LRFD (``diseno_elemento.disenar_*_combos``) — incluido el estribo de columna real
(cortante + confinamiento) — y empaqueta el armado + el combo gobernante + su demanda factorada +
cumple, reusando la geometría de ``viz.armado``. Función pura.

Unidades del DTO: metros (posiciones, estribo) y N/N·m (demanda), como la escena.
"""
from __future__ import annotations

from motor_fea import diseno_elemento
from motor_fea.core.casos import esfuerzos_por_caso
from motor_fea.core.modelo import ModeloEstructural
from motor_fea.viz import armado
from motor_fea.viz.escena import _clasificar, _dimensiones


def calcular_diseno(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """DisenoDTO: armado LRFD + combo gobernante + demanda factorada + estribo de columna diseñado."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    epc = esfuerzos_por_caso(modelo)
    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = armado._diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        esf_por_caso = {caso: epc[caso][e.id] for caso in epc}
        if _clasificar(ni, nj) == "columna":
            d = diseno_elemento.disenar_columna_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            long = armado._posiciones_columna(b, h, recubrimiento, d.numero_barra, d.n_barras)
            d_estribo_m = armado._diametro_m(d.estribo.numero_barra)
            s = d.estribo.espaciamiento / 1000.0
            tipo, designacion, cumple, combo, estribo_txt = (
                "columna", d.disponer, d.cumple, d.combo_gobernante, d.estribo.disponer)
        else:
            d = diseno_elemento.disenar_viga_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            d_estribo_m = d_est
            s = d.estribo.espaciamiento / 1000.0
            tipo, designacion, cumple, combo, estribo_txt = (
                "viga", d.disponer, d.cumple, d.combo_flexion, "")
        pu, mu, vu = diseno_elemento._demanda_por_combo(esf_por_caso)[combo]
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_estribo_m, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion,
            "demanda": {"pu": abs(pu), "mu": abs(mu), "vu": abs(vu)},
            "combo": combo, "estribo_txt": estribo_txt, "cumple": cumple,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
```

- [ ] **Step 4: Correr — PASS**: `PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py tests/test_servidor.py -q`

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/viz/diseno.py tests/test_diseno_visual.py tests/test_servidor.py
git commit -m "feat(viz): estribo de columna disenado (cortante+confinamiento) en el DTO

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Etiqueta del visor + README + verificación final

**Files:** Modify `src/motor_fea/viz/static/app.js`, `README.md`

- [ ] **Step 1: Estribo en la etiqueta** — en `src/motor_fea/viz/static/app.js`, reemplazar la función `mostrarDiseno` ENTERA por:
```javascript
function mostrarDiseno(el) {
  const kN = (n) => (n / 1000).toFixed(0);
  const dem = el.tipo === 'columna'
    ? `Pu=${kN(el.demanda.pu)} kN, Mu=${kN(el.demanda.mu)} kN·m`
    : `Mu=${kN(el.demanda.mu)} kN·m, Vu=${kN(el.demanda.vu)} kN`;
  const est = el.estribo_txt ? ` · ${el.estribo_txt}` : '';
  info.textContent = `${el.designacion} · combo ${el.combo} · ${dem}${est} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
}
```

- [ ] **Step 2: Validación estática**: `cp src/motor_fea/viz/static/app.js /tmp/app5c.mjs && node --check /tmp/app5c.mjs` → exit 0; `grep -n 'estribo_txt' src/motor_fea/viz/static/app.js`.

- [ ] **Step 3: README** — junto al párrafo de `/diseno` (Fase 5A), añadir:
```markdown
Las columnas traen además su **estribo diseñado** (cortante con axial ACI §22.5.6.1 + confinamiento
sísmico §18.7.5.4, escalando la barra del estribo hasta confinar); la jaula del visor dibuja la
separación real y la etiqueta la muestra (`E#3@100`).
```

- [ ] **Step 4: Suite completa** — `PYTHONPATH=src:tests python -m pytest -q`
Expected: ~195 passed (188 + 5 aci318 + 1 diseno_elemento + 1 viz/servidor). Reportar el conteo; si algo falla, STOP/BLOCKED.

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/viz/static/app.js README.md
git commit -m "feat(viz): mostrar el estribo de columna en la etiqueta + docs

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~195); sin regresión.
2. Las columnas en `/diseno` traen estribo diseñado (cortante + confinamiento), separación y `cumple` reales.
3. El visor dibuja la separación de estribos diseñada y la etiqueta muestra `E#3@s` en columnas.

## Notas de revisión

- **Aditivo en aci318/diseno_elemento**: las funciones de viga y P-M no cambian; el estribo de columna se suma.
- **Signo del axial**: `_demanda_por_combo` da Pu en tracción+; el orquestador pasa `-Pu` a
  `disenar_estribo_columna` (que espera compresión+).
- **Escalado del estribo**: #3→#5 hasta confinar; si ni #5 confina → `cumple=False`.
- **Simplificaciones** (spec §8): separación confinada en toda la altura; `so=150`; término de alto axial omitido; `n_ramas=2`.

# Fase 5B.2: flexión biaxial en el visor — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** Surfacing My/Mz por separado + la utilización biaxial del combo gobernante de la columna en `/diseno` y la etiqueta del visor.

**Architecture:** `DisenoColumnaCombos` gana `muy/muz/utilizacion` (poblados en `disenar_columna_combos`); `viz/diseno` los pasa al DTO; `app.js` los muestra. `core/`/`aci318`/`armado` intactos.

**Spec:** `docs/superpowers/specs/2026-06-06-fase5b2-biaxial-visor-design.md`

---

## Task 1: `DisenoColumnaCombos` + muy/muz/utilizacion (motor)

**Files:** Modify `src/motor_fea/diseno_elemento.py`; Test `tests/test_combinaciones_diseno.py`

- [ ] **Step 1: Test que falla** — añadir al final de `tests/test_combinaciones_diseno.py`:
```python
def test_columna_combos_trae_biaxial():
    m = _columna_xy([CargaNodal(2, fz=-100000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="W"),
                     CargaNodal(2, fy=15000.0, caso="W")])
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.40, h=0.40,
                                               fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d.muy > 0 and d.muz > 0           # momento en ambos ejes
    assert d.utilizacion > 0
    if d.cumple:
        assert d.utilizacion <= 1.0 + 1e-9
```

- [ ] **Step 2: Correr — FAIL** (`AttributeError: 'DisenoColumnaCombos' object has no attribute 'muy'`):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py::test_columna_combos_trae_biaxial -q` (use `.venv/bin/pytest` si falta).

- [ ] **Step 3: Implementar** — en `src/motor_fea/diseno_elemento.py`:

(a) A `DisenoColumnaCombos` (frozen dataclass) añadir TRES campos al final (después de `combo_cortante: str`):
```python
    combo_cortante: str
    muy: float          # N·m (combo gobernante)
    muz: float          # N·m
    utilizacion: float  # (Muy/φMny + Muz/φMnz) en la sección diseñada
```

(b) REEMPLAZAR `disenar_columna_combos` ENTERA por (idéntica a 5B.1 salvo los TRES args extra al final de cada `return`):
```python
def disenar_columna_combos(esf_por_caso: dict[str, EsfuerzosElemento], b: float, h: float,
                           fc: float = 21.0, fy: float = 420.0, recubrimiento: float = 0.04,
                           num: int = 8) -> DisenoColumnaCombos:
    """Diseña una columna (P-M-M biaxial + estribo) cubriendo todos los combos LRFD. b,h,rec en m."""
    if b <= 0 or h <= 0 or fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fy y recubrimiento deben ser positivos.")
    b_mm, h_mm, rec_mm = b * 1000.0, h * 1000.0, recubrimiento * 1000.0
    if h_mm - 2 * rec_mm <= 0 or b_mm - 2 * rec_mm <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    biax = _demanda_biaxial_por_combo(esf_por_caso)          # {combo: (Pu, Muy, Muz, Vu)} N/N·m
    combo_v = max(biax, key=lambda k: abs(biax[k][3]))
    estribo = aci318.disenar_estribo_columna(abs(biax[combo_v][3]), -biax[combo_v][0],
                                             b_mm, h_mm, fc, aci318._diametro_barra(num), rec_mm, fy)
    dem = {k: (abs(P), abs(My) * 1000.0, abs(Mz) * 1000.0) for k, (P, My, Mz, _V) in biax.items()}
    ag = b_mm * h_mm
    area = aci318.AREAS_BARRA_MM2[num]
    n = max(4, math.ceil(0.01 * ag / area))
    ultimo_n = n
    while n * area / ag <= 0.08:
        capas_y, capas_z = aci318._capas_biaxial(b_mm, h_mm, rec_mm, num, n)
        pmax = aci318.axial_maxima_diseno(ag, n * area, fc, fy)
        if all(pu <= pmax and aci318.factor_biaxial(pu, muy, muz, b_mm, h_mm, fc, fy, capas_y, capas_z) <= 1.0
               for pu, muy, muz in dem.values()):
            gob = _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax)
            util = aci318.factor_biaxial(dem[gob][0], dem[gob][1], dem[gob][2], b_mm, h_mm, fc, fy, capas_y, capas_z)
            return DisenoColumnaCombos(dem[gob][0], math.hypot(dem[gob][1], dem[gob][2]), num, n,
                                       n * area / ag, estribo.cumple, f"{n}#{num}", gob, estribo, combo_v,
                                       abs(biax[gob][1]), abs(biax[gob][2]), util)
        ultimo_n = n
        n += 1
    capas_y, capas_z = aci318._capas_biaxial(b_mm, h_mm, rec_mm, num, ultimo_n)
    pmax = aci318.axial_maxima_diseno(ag, ultimo_n * area, fc, fy)
    gob = _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax)
    util = aci318.factor_biaxial(dem[gob][0], dem[gob][1], dem[gob][2], b_mm, h_mm, fc, fy, capas_y, capas_z)
    return DisenoColumnaCombos(dem[gob][0], math.hypot(dem[gob][1], dem[gob][2]), num, ultimo_n,
                               ultimo_n * area / ag, False, "SECCIÓN INSUFICIENTE", gob, estribo, combo_v,
                               abs(biax[gob][1]), abs(biax[gob][2]), util)
```

- [ ] **Step 4: Correr los tests de combinaciones — PASS** (los previos siguen verdes; +1):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`
(Campos aditivos; los `return` ahora pasan 13 args posicionales = 13 campos del dataclass — verificar el conteo.)

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/diseno_elemento.py tests/test_combinaciones_diseno.py
git commit -m "feat(diseno): DisenoColumnaCombos reporta muy/muz/utilizacion biaxial

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: DTO + etiqueta del visor

**Files:** Modify `src/motor_fea/viz/diseno.py`, `src/motor_fea/viz/static/app.js`; Test `tests/test_diseno_visual.py`, `tests/test_servidor.py`

- [ ] **Step 1: Tests que fallan** — añadir al final de `tests/test_diseno_visual.py`:
```python
def test_columna_dto_biaxial():
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3.0)]
    m.materiales.append(Material(1, E=2.0e10))
    bc = 0.40
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += [CargaNodal(2, fz=-100000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="W"),
                 CargaNodal(2, fy=15000.0, caso="W")]
    el = diseno.calcular_diseno(m, fc=28.0, fy=420.0, recubrimiento=0.05)["elementos"][0]
    assert el["tipo"] == "columna"
    assert el["muy"] > 0 and el["muz"] > 0 and el["utilizacion"] > 0
```
Y en `tests/test_servidor.py`, al final de `test_diseno_tiene_combo_y_casos`:
```python
    assert all("utilizacion" in e for e in data["elementos"])
```

- [ ] **Step 2: Correr — FAIL** (`KeyError: 'muy'`): `PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py -q`

- [ ] **Step 3: Implementar `viz/diseno.py`** — reemplazar el bloque del `for e in modelo.elementos:` (desde `if _clasificar(...) == "columna":` hasta el `elementos.append({...})`) por:
```python
        if _clasificar(ni, nj) == "columna":
            d = diseno_elemento.disenar_columna_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            long = armado._posiciones_columna(b, h, recubrimiento, d.numero_barra, d.n_barras)
            d_estribo_m = armado._diametro_m(d.estribo.numero_barra)
            s = d.estribo.espaciamiento / 1000.0
            tipo, designacion, cumple, combo, estribo_txt = (
                "columna", d.disponer, d.cumple, d.combo_gobernante, d.estribo.disponer)
            muy_e, muz_e, util_e = d.muy, d.muz, d.utilizacion
        else:
            d = diseno_elemento.disenar_viga_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            d_estribo_m = d_est
            s = d.estribo.espaciamiento / 1000.0
            tipo, designacion, cumple, combo, estribo_txt = (
                "viga", d.disponer, d.cumple, d.combo_flexion, "")
            muy_e, muz_e, util_e = 0.0, 0.0, 0.0
        pu, mu, vu = diseno_elemento._demanda_por_combo(esf_por_caso)[combo]
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_estribo_m, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion,
            "demanda": {"pu": abs(pu), "mu": abs(mu), "vu": abs(vu)},
            "muy": muy_e, "muz": muz_e, "utilizacion": util_e,
            "combo": combo, "estribo_txt": estribo_txt, "cumple": cumple,
        })
```

- [ ] **Step 4: Implementar `app.js`** — reemplazar `mostrarDiseno` ENTERA por:
```javascript
function mostrarDiseno(el) {
  const kN = (n) => (n / 1000).toFixed(0);
  const est = el.estribo_txt ? ` · ${el.estribo_txt}` : '';
  const dem = el.tipo === 'columna'
    ? `Pu=${kN(el.demanda.pu)} kN, My=${kN(el.muy)} Mz=${kN(el.muz)} kN·m (u=${el.utilizacion.toFixed(2)})`
    : `Mu=${kN(el.demanda.mu)} kN·m, Vu=${kN(el.demanda.vu)} kN`;
  info.textContent = `${el.designacion} · combo ${el.combo} · ${dem}${est} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
}
```

- [ ] **Step 5: Validación + suite**
1. `cp src/motor_fea/viz/static/app.js /tmp/app5b2.mjs && node --check /tmp/app5b2.mjs` → exit 0.
2. `PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py tests/test_servidor.py -q` → PASS.
3. Suite completa: `PYTHONPATH=src:tests python -m pytest -q` → ~202 passed. Reportar el conteo; si algo falla, STOP/BLOCKED.

- [ ] **Step 6: Commit**
```bash
git add src/motor_fea/viz/diseno.py src/motor_fea/viz/static/app.js tests/test_diseno_visual.py tests/test_servidor.py
git commit -m "feat(viz): exponer My/Mz/utilizacion biaxial de columna en /diseno y la etiqueta

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación
1. Suite verde (~202); sin regresión.
2. `/diseno` reporta `muy`/`muz`/`utilizacion` por columna; la etiqueta muestra `My=… Mz=… (u=…)`.

## Notas de revisión
- **Aditivo:** `DisenoColumnaCombos` gana 3 campos (los `return` pasan a 13 args); el visor los lee. `aci318`/viga/single-case intactos.
- **Unidades:** `muy/muz` en N·m (como la demanda); `utilizacion` adimensional; la etiqueta convierte a kN·m.
- **Viga:** `muy=muz=utilizacion=0`; su etiqueta no cambia (Mu/Vu).

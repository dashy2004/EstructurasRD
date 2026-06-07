# Fase 5B.1: flexión biaxial de columnas (motor) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** Diseñar columnas a flexión biaxial (contorno de Bresler α=1, barras de perímetro) reemplazando el `Mu=max|My|,|Mz|` uniaxial en `disenar_columna_combos`.

**Architecture:** Aditivo en `aci318` (perímetro + capas por eje + factor biaxial); `diseno_elemento` gana demanda biaxial y `disenar_columna_combos` chequea el contorno por combo. `core/`/`viz/`/`api/` intactos.

**Spec:** `docs/superpowers/specs/2026-06-06-fase5b1-flexion-biaxial-design.md`

---

## Task 1: `aci318` — primitivas biaxiales (aditivo)

**Files:** Modify `src/motor_fea/normativa/aci318.py`; Test `tests/test_diseno_marco.py`

- [ ] **Step 1: Tests que fallan** — añadir al final de `tests/test_diseno_marco.py`:
```python
def test_capas_biaxial_cuadrada_simetrica():
    capas_y, capas_z = aci318._capas_biaxial(400, 400, 50, 8, 8)
    assert sum(As for _, As in capas_y) == pytest.approx(sum(As for _, As in capas_z))
    assert sorted(di for di, _ in capas_y) == pytest.approx(sorted(di for di, _ in capas_z))


def test_factor_biaxial_uniaxial_y_biaxial():
    b = h = 400.0
    capas_y, capas_z = aci318._capas_biaxial(b, h, 50, 8, 8)
    diag_y = aci318.diagrama_interaccion(b, h, 28, 420, capas_z)
    p = diag_y[20]
    pu = max(p.phi_pn, 1.0)
    cmy = aci318.momento_capacidad(pu, diag_y)
    assert cmy > 0
    # uniaxial (muz=0, muy=cmy) → factor ≈ 1
    assert aci318.factor_biaxial(pu, cmy, 0.0, b, h, 28, 420, capas_y, capas_z) == pytest.approx(1.0, rel=1e-6)
    # biaxial (muy=muz=cmy) en sección cuadrada simétrica → ≈ 2
    assert aci318.factor_biaxial(pu, cmy, cmy, b, h, 28, 420, capas_y, capas_z) == pytest.approx(2.0, rel=1e-3)


def test_factor_biaxial_fuera_de_rango_inf():
    import math as _m
    capas_y, capas_z = aci318._capas_biaxial(400, 400, 50, 8, 8)
    # pu axial enorme (fuera del diagrama) → capacidad 0 → factor inf
    assert aci318.factor_biaxial(1.0e9, 10.0, 10.0, 400, 400, 28, 420, capas_y, capas_z) == _m.inf
```

- [ ] **Step 2: Correr — FAIL** (`AttributeError: ... '_capas_biaxial'`): `PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q` (use `.venv/bin/pytest` si falta).

- [ ] **Step 3: Implementar** — añadir al **final** de `src/motor_fea/normativa/aci318.py` (ya tiene `math`, `AREAS_BARRA_MM2`, `_diametro_barra`, `diagrama_interaccion`, `momento_capacidad`):
```python
# ===================== Flexión biaxial de columnas (Fase 5B.1) =====================
def _perimetro_columna(n: int, ox: float, oy: float) -> list[tuple[float, float]]:
    """n posiciones (py, pz) equiespaciadas por arco en el perímetro de semi-ejes (ox, oy)."""
    esquinas = [(-ox, -oy), (ox, -oy), (ox, oy), (-ox, oy)]
    lados = [2 * ox, 2 * oy, 2 * ox, 2 * oy]
    per = 2 * (2 * ox + 2 * oy)
    if per <= 0:
        return [(0.0, 0.0)] * n
    puntos: list[tuple[float, float]] = []
    for k in range(n):
        s = k * per / n
        for lado in range(4):
            if s <= lados[lado] or lado == 3:
                x0, y0 = esquinas[lado]
                x1, y1 = esquinas[(lado + 1) % 4]
                f = s / lados[lado] if lados[lado] > 0 else 0.0
                puntos.append((x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
                break
            s -= lados[lado]
    return puntos


def _capas_biaxial(b: float, h: float, rec: float, num: int, n: int):
    """(capas_y, capas_z) de n barras #num del perímetro, proyectadas a cada eje (mm).

    capas_z: agrupadas por z, di = h/2 − pz (prof. en z) → capacidad de My (prof. h, ancho b).
    capas_y: agrupadas por y, di = b/2 − py (prof. en y) → capacidad de Mz (prof. b, ancho h).
    """
    d = _diametro_barra(num)
    area = AREAS_BARRA_MM2[num]
    ox = max(0.0, b / 2.0 - rec - d / 2.0)
    oy = max(0.0, h / 2.0 - rec - d / 2.0)
    by_z: dict[float, float] = {}
    by_y: dict[float, float] = {}
    for py, pz in _perimetro_columna(n, ox, oy):
        kz, ky = round(pz, 3), round(py, 3)
        by_z[kz] = by_z.get(kz, 0.0) + area
        by_y[ky] = by_y.get(ky, 0.0) + area
    capas_z = [(h / 2.0 - pz, As) for pz, As in by_z.items()]
    capas_y = [(b / 2.0 - py, As) for py, As in by_y.items()]
    return capas_y, capas_z


def factor_biaxial(pu: float, muy: float, muz: float, b: float, h: float, fc: float, fy: float,
                   capas_y, capas_z, alfa: float = 1.0) -> float:
    """Utilización biaxial — contorno de carga de Bresler (ACI R10.3): (Muy/φMny)^α + (Muz/φMnz)^α.

    pu, muy, muz en N, N·mm; ∞ si alguna capacidad de momento ≤ 0 (pu fuera del diagrama).
    """
    phi_mny = momento_capacidad(pu, diagrama_interaccion(b, h, fc, fy, capas_z))
    phi_mnz = momento_capacidad(pu, diagrama_interaccion(h, b, fc, fy, capas_y))
    if phi_mny <= 0.0 or phi_mnz <= 0.0:
        return math.inf
    return (muy / phi_mny) ** alfa + (muz / phi_mnz) ** alfa
```

- [ ] **Step 4: Correr — PASS** (3 nuevos): `PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q`

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/normativa/aci318.py tests/test_diseno_marco.py
git commit -m "feat(aci318): primitivas biaxiales (perimetro + capas por eje + contorno Bresler)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `diseno_elemento` — `disenar_columna_combos` biaxial

**Files:** Modify `src/motor_fea/diseno_elemento.py`; Test `tests/test_combinaciones_diseno.py`

- [ ] **Step 1: Test que falla** — añadir al final de `tests/test_combinaciones_diseno.py`:
```python
def _columna_xy(cargas, bc=0.40):
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, _L)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += cargas
    return m


def test_columna_biaxial_no_menos_barras_que_uniaxial():
    uni = _columna_xy([CargaNodal(2, fz=-100000.0, caso="D"), CargaNodal(2, fx=30000.0, caso="W")])
    bia = _columna_xy([CargaNodal(2, fz=-100000.0, caso="D"), CargaNodal(2, fx=30000.0, caso="W"),
                       CargaNodal(2, fy=30000.0, caso="W")])
    d_uni = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(uni), 1), b=0.40, h=0.40,
                                                   fc=28.0, fy=420.0, recubrimiento=0.05)
    d_bia = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(bia), 1), b=0.40, h=0.40,
                                                   fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d_bia.n_barras >= d_uni.n_barras          # biaxial nunca necesita menos acero
    assert d_bia.combo_gobernante
```

- [ ] **Step 2: Correr — FAIL** (el test nuevo):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py::test_columna_biaxial_no_menos_barras_que_uniaxial -q`

- [ ] **Step 3: Implementar** — en `src/motor_fea/diseno_elemento.py`:

(a) Añadir (después de `_demanda_por_combo`):
```python
def _escalares_biaxial_por_caso(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float, float]]:
    """{caso: (P, My, Mz, V)} — P con signo (N); My=max|My|, Mz=max|Mz| (N·m); V=max|Vy|,|Vz| (N)."""
    out: dict[str, tuple[float, float, float, float]] = {}
    for caso, esf in esf_por_caso.items():
        my = mz = vu = 0.0
        for _s, _n, vy, vz, _t, m_y, m_z in esf.diagrama(21):
            my = max(my, abs(m_y))
            mz = max(mz, abs(m_z))
            vu = max(vu, abs(vy), abs(vz))
        out[caso] = (esf.axial, my, mz, vu)
    return out


def _demanda_biaxial_por_combo(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float, float]]:
    """{combo: (Pu, Muy, Muz, Vu)} (N, N·m, N·m, N) — LRFD; cada componente combinada por separado."""
    esc = _escalares_biaxial_por_caso(esf_por_caso)
    cp = combinaciones_resistencia(**{c: v[0] for c, v in esc.items()})
    cmy = combinaciones_resistencia(**{c: v[1] for c, v in esc.items()})
    cmz = combinaciones_resistencia(**{c: v[2] for c, v in esc.items()})
    cv = combinaciones_resistencia(**{c: v[3] for c, v in esc.items()})
    return {k: (cp[k], cmy[k], cmz[k], cv[k]) for k in cp}


def _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax) -> str:
    """Combo con mayor utilización biaxial (factor_biaxial); desempate por pu."""
    def clave(pu, muy, muz):
        f = aci318.factor_biaxial(pu, muy, muz, b_mm, h_mm, fc, fy, capas_y, capas_z)
        r_p = pu / pmax if pmax > 0 else math.inf
        return (max(f, r_p), pu)
    return max(dem, key=lambda k: clave(*dem[k]))
```

(b) ELIMINAR la función `_gobernante_columna` (queda muerta tras este cambio; sólo la usaba `disenar_columna_combos`).

(c) REEMPLAZAR `disenar_columna_combos` ENTERA por:
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
    # Estribo (cortante con axial + confinamiento) para el combo de mayor |Vu|; pu compresión+ = -axial.
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
            return DisenoColumnaCombos(dem[gob][0], math.hypot(dem[gob][1], dem[gob][2]), num, n,
                                       n * area / ag, estribo.cumple, f"{n}#{num}", gob, estribo, combo_v)
        ultimo_n = n
        n += 1
    capas_y, capas_z = aci318._capas_biaxial(b_mm, h_mm, rec_mm, num, ultimo_n)
    pmax = aci318.axial_maxima_diseno(ag, ultimo_n * area, fc, fy)
    gob = _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax)
    return DisenoColumnaCombos(dem[gob][0], math.hypot(dem[gob][1], dem[gob][2]), num, ultimo_n,
                               ultimo_n * area / ag, False, "SECCIÓN INSUFICIENTE", gob, estribo, combo_v)
```

- [ ] **Step 4: Correr los tests de combinaciones — PASS**
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`
Los tests 5A.1 (`test_columna_solo_D_gobierna_combo_1`, `test_columna_combos_no_menos_barras_que_caso_D`,
`test_columna_combos_insuficiente`, `test_columna_combos_trae_estribo`, `test_columna_caso_reversible_no_rompe`)
DEBERÍAN seguir verdes (biaxial nunca pide menos acero; solo-D axial → factor≈0 → combo "1" gobierna por axial;
0.20 sobre-axial → insuficiente). **Si alguno falla por un cambio legítimo de `combo_gobernante`/`n_barras`**,
actualizá la aserción al valor biaxial correcto y documentá por qué; NO debilites la intención del test.
Reportá cualquier cambio.

- [ ] **Step 5: Suite completa (regresión)** — `PYTHONPATH=src:tests python -m pytest -q`
Expected: ~201 passed. `aci318` (viga/estribo/P-M uniaxial), 4b/5A.2/5C viz y viga no se tocan → sin regresión.
Si algo de viga/estribo/viz falla, STOP/BLOCKED (sería un bug del rewrite). Reportar el conteo exacto.

- [ ] **Step 6: Commit**
```bash
git add src/motor_fea/diseno_elemento.py tests/test_combinaciones_diseno.py
git commit -m "feat(diseno): disenar_columna_combos biaxial (contorno de Bresler, perimetro)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación
1. Suite verde (~201); sin regresión en viga/estribo/4b/5A.2/5C.
2. `disenar_columna_combos` diseña biaxial: una columna con My **y** Mz requiere ≥ acero que con un solo eje.

## Notas de revisión
- **Aditivo en aci318**; en diseno_elemento solo cambia `disenar_columna_combos` + se agregan helpers biaxiales + se elimina el `_gobernante_columna` uniaxial muerto. Viga y demanda uniaxial (para el estribo) intactas.
- **Ejes:** `factor_biaxial` usa `diagrama_interaccion(b,h,…,capas_z)` para φMny (prof. h) y `(h,b,…,capas_y)` para φMnz (prof. b). Para n=4 las capas se reducen al modelo de 2 capas por eje.
- **`mu` reportado** = resultante `√(Muy²+Muz²)` del combo gobernante (los campos `pu/mu` del dataclass no los lee el visor).
- **α=1.0** conservador (documentado).

# Rebanada A · Modelo canónico del edificio — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar en el motor Python el modelo canónico del edificio como contrato JSON versionado (dataclasses + validación + (de)serialización), resolviendo el bug de niveles/elevaciones.

**Architecture:** Paquete nuevo `motor_fea.edificio`, independiente del `ModeloEstructural` FEA de `motor_fea.core`. `modelo.py` define dataclasses frozen de stdlib (Proyecto → Edificio → Nivel → Losa, y ElementoVertical = Columna|Muro continuas) con `Proyecto.validar() -> list[str]` al estilo del motor. `contrato.py` traduce entre el JSON versionado y esos objetos (round-trip exacto). Sin NumPy, sin I/O fuera de `contrato.py`, sin identificadores `losasplus`.

**Tech Stack:** Python 3.11, dataclasses stdlib, pytest. Runner del repo: `.venv/bin/pytest`. Layout src (`src/motor_fea/...`, tests en `tests/`).

**Spec de referencia:** `docs/superpowers/specs/2026-06-15-modelo-canonico-edificio-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/edificio/__init__.py` | Marcador de paquete + re-exports públicos |
| `src/motor_fea/edificio/modelo.py` | Dataclasses del modelo canónico + `Proyecto.validar()` + helpers derivados (orden de niveles, passing-through, propagación de cota) |
| `src/motor_fea/edificio/contrato.py` | (De)serialización JSON versionada: `proyecto_desde_dict`/`proyecto_a_dict` + texto |
| `tests/test_edificio_modelo.py` | Construcción de entidades, helpers derivados, validación, regresiones del bug |
| `tests/test_edificio_contrato.py` | Round-trip JSON, versión, escenarios del bug end-to-end |

**Decisiones de modelado bloqueadas aquí:**
- `Columna` y `Muro` son dataclasses separadas (no una con campos opcionales): el JSON discrimina por `tipo`, pero en Python la identidad de clase es el discriminador. El contenedor `Edificio.elementos_verticales` es una lista mixta de ambas.
- La `cota` vive **solo** en `Nivel`. La `Losa` guarda su contorno **en planta** (`(x,y)`); su elevación 3D la aporta el nivel (`Nivel.puntos_losa_3d`). Esto resuelve la mitad #1 del bug por construcción.
- El passing-through (`Edificio.niveles_atravesados`) es **derivado**, no almacenado: se computa de `cota_base/cota_tope` vs cotas de niveles. Resuelve la mitad #2.
- Catálogo de tipos de losa fijado para A: `TIPOS_LOSA = {"maciza", "aligerada", "reticular"}`. Ampliable en rebanadas siguientes sin romper el contrato (es validación, no estructura).

---

## Task 1: Esqueleto del paquete + Losa / CargasLosa / catálogo de tipos

**Files:**
- Create: `src/motor_fea/edificio/__init__.py`
- Create: `src/motor_fea/edificio/modelo.py`
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py
"""Tests del modelo canónico del edificio (Rebanada A)."""


def test_losa_construccion_y_defaults():
    from motor_fea.edificio.modelo import CargasLosa, Losa, TIPOS_LOSA

    assert "maciza" in TIPOS_LOSA
    l = Losa(id=1, tipo="maciza", espesor=0.20,
             puntos=((0, 0), (5, 0), (5, 5), (0, 5)))
    assert l.cargas == CargasLosa(0.0, 0.0)          # cargas por defecto en cero
    l2 = Losa(id=2, tipo="aligerada", espesor=0.25,
              puntos=((0, 0), (5, 0), (5, 5)),
              cargas=CargasLosa(muerta=1.5, viva=2.0))
    assert (l2.cargas.muerta, l2.cargas.viva) == (1.5, 2.0)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_losa_construccion_y_defaults -v`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.edificio'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/__init__.py
"""Modelo canónico del edificio (capa de autoría EstructurasRD).

Fuente de verdad del contrato de autoría: de aquí lo consumen el FEA (vía
síntesis, rebanada siguiente), el visor y la memoria. Distinto del
``ModeloEstructural`` de ``motor_fea.core`` (malla FEA de bajo nivel).
"""
```

```python
# src/motor_fea/edificio/modelo.py
"""Dataclasses del modelo canónico del edificio + validación (Rebanada A).

Jerarquía: Proyecto → Edificio → Nivel → Losa, con elementos verticales
(Columna/Muro) CONTINUOS a nivel de edificio (atraviesan niveles). Unidades SI:
longitudes en metros, Z hacia arriba. Sin NumPy, sin I/O (eso vive en
``contrato.py``).
"""
from __future__ import annotations

from dataclasses import dataclass, field

# Catálogo de tipos de losa conocidos (validación). Ampliable sin romper el contrato.
TIPOS_LOSA = frozenset({"maciza", "aligerada", "reticular"})


@dataclass(frozen=True)
class CargasLosa:
    """Cargas de servicio de una losa, en kN/m² (muerta adicional y viva)."""
    muerta: float = 0.0
    viva: float = 0.0


@dataclass(frozen=True)
class Losa:
    """Losa de una planta. ``puntos`` es el contorno EN PLANTA ((x, y), ...);
    la elevación 3D la aporta el ``Nivel`` (no se almacena acá)."""
    id: int
    tipo: str
    espesor: float            # m
    puntos: tuple             # ((x, y), ...) — contorno en planta, ≥3 puntos
    cargas: CargasLosa = CargasLosa()
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_losa_construccion_y_defaults -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/__init__.py src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): paquete edificio + Losa/CargasLosa/catálogo de tipos"
```

---

## Task 2: Nivel + propagación de cota (regresión bug #1 y "nombre independiente")

**Files:**
- Modify: `src/motor_fea/edificio/modelo.py` (añadir `Nivel`)
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py  (añadir)
def test_nivel_propaga_cota_a_sus_losas():
    from motor_fea.edificio.modelo import Losa, Nivel

    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)))
    nivel = Nivel(id=1, nombre="Primer nivel", cota=3.0, losas=(losa,))

    pts3d = nivel.puntos_losa_3d(losa)
    assert all(z == 3.0 for (_x, _y, z) in pts3d)     # la cota del nivel baja a la losa
    assert pts3d[0] == [0, 0, 3.0]


def test_nombre_del_nivel_es_independiente_de_la_losa():
    from motor_fea.edificio.modelo import Losa, Nivel

    losa = Losa(id=7, tipo="maciza", espesor=0.20, puntos=((0, 0), (1, 0), (1, 1)))
    nivel = Nivel(id=1, nombre="Mezzanine", cota=0.0, losas=(losa,))
    assert nivel.nombre == "Mezzanine"                # no derivado de la losa/sistema
    assert not hasattr(losa, "nombre")                # la losa no impone nombre al nivel
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py -k nivel -v`
Expected: FAIL con `ImportError: cannot import name 'Nivel'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/modelo.py  (añadir tras Losa)
@dataclass(frozen=True)
class Nivel:
    """Planta del edificio (nivel = sistema unificado). ``cota`` es la única
    fuente de la elevación; las losas la heredan vía ``puntos_losa_3d``."""
    id: int
    nombre: str               # libre, independiente del nombre de las losas
    cota: float               # m, Z arriba
    losas: tuple = ()         # (Losa, ...)

    def puntos_losa_3d(self, losa: "Losa") -> list:
        """Contorno 3D de una losa de este nivel: su (x, y) en planta a ``cota``."""
        return [[x, y, self.cota] for (x, y) in losa.puntos]
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py -k nivel -v`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): Nivel con cota única + propagación 3D a losas (bug #1)"
```

---

## Task 3: Verticales continuas — Zapata / Columna / Muro

**Files:**
- Modify: `src/motor_fea/edificio/modelo.py` (añadir `Zapata`, `Columna`, `Muro`)
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py  (añadir)
def test_columna_y_muro_continuos():
    from motor_fea.edificio.modelo import Columna, Muro, Zapata

    col = Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=6.0, material="H210",
                  zapata=Zapata(ancho=1.2, largo=1.2, peralte=0.4))
    assert (col.cota_base, col.cota_tope) == (0.0, 6.0)   # rango vertical continuo
    assert col.zapata.ancho == 1.2

    muro = Muro(id=2, linea=((0, 0), (0, 5)), espesor=0.20,
                cota_base=0.0, cota_tope=6.0, material="H210")
    assert muro.zapata is None                            # zapata opcional
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_columna_y_muro_continuos -v`
Expected: FAIL con `ImportError: cannot import name 'Columna'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/modelo.py  (añadir tras Nivel)
@dataclass(frozen=True)
class Zapata:
    """Fundación aislada en la base de una vertical. Dimensiones en m."""
    ancho: float
    largo: float
    peralte: float


@dataclass(frozen=True)
class Columna:
    """Columna continua. ``posicion`` = (x, y) en planta; atraviesa
    ``cota_base → cota_tope`` (m)."""
    id: int
    posicion: tuple           # (x, y)
    base: float               # m (sección)
    peralte: float            # m (sección)
    cota_base: float
    cota_tope: float
    material: str
    zapata: "Zapata | None" = None


@dataclass(frozen=True)
class Muro:
    """Muro continuo. ``linea`` = ((x1, y1), (x2, y2)) en planta; atraviesa
    ``cota_base → cota_tope`` (m)."""
    id: int
    linea: tuple              # ((x1, y1), (x2, y2))
    espesor: float            # m (sección)
    cota_base: float
    cota_tope: float
    material: str
    zapata: "Zapata | None" = None
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_columna_y_muro_continuos -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): Columna/Muro continuos (cota_base→cota_tope) + Zapata"
```

---

## Task 4: Contenedores — Metadata / CargasGlobales / Edificio / Proyecto

**Files:**
- Modify: `src/motor_fea/edificio/modelo.py` (añadir contenedores + helper de orden)
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py  (añadir)
def test_proyecto_contenedores_y_orden_de_niveles():
    from motor_fea.edificio.modelo import (
        CargasGlobales, Edificio, Metadata, Nivel, Proyecto,
    )

    n1 = Nivel(id=1, nombre="N1", cota=0.0)
    n2 = Nivel(id=2, nombre="N2", cota=3.0)
    edi = Edificio(id=1, nombre="Bloque A", niveles=[n2, n1])   # desordenados a propósito
    proy = Proyecto(metadata=Metadata(nombre="Demo"),
                    cargas_globales=CargasGlobales(muerta_adicional=1.5, viva=2.0),
                    combinaciones=["1.2D+1.6L"], edificios=[edi])

    assert [n.cota for n in edi.niveles_ordenados()] == [0.0, 3.0]   # ordena por cota
    assert edi.cota_minima() == 0.0
    assert proy.metadata.nombre == "Demo"
    assert proy.combinaciones == ["1.2D+1.6L"]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_proyecto_contenedores_y_orden_de_niveles -v`
Expected: FAIL con `ImportError: cannot import name 'Proyecto'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/modelo.py  (añadir tras Muro)
@dataclass(frozen=True)
class Metadata:
    """Metadatos del proyecto (todos opcionales)."""
    nombre: str = ""
    autor: str = ""
    codigo_obra: str = ""
    ubicacion: str = ""
    fecha: str = ""


@dataclass(frozen=True)
class CargasGlobales:
    """Cargas globales del proyecto, en kN/m²."""
    muerta_adicional: float = 0.0
    viva: float = 0.0


@dataclass
class Edificio:
    """Edificio. Las verticales viven acá porque atraviesan niveles."""
    id: int
    nombre: str
    niveles: list = field(default_factory=list)               # [Nivel]
    elementos_verticales: list = field(default_factory=list)  # [Columna | Muro]

    def niveles_ordenados(self) -> list:
        """Niveles ordenados por cota creciente."""
        return sorted(self.niveles, key=lambda n: n.cota)

    def cota_minima(self) -> float:
        """Cota mínima del edificio (referencia de fundación). 0.0 si no hay niveles."""
        return min((n.cota for n in self.niveles), default=0.0)


@dataclass
class Proyecto:
    """Raíz del modelo canónico."""
    metadata: Metadata = field(default_factory=Metadata)
    cargas_globales: CargasGlobales = field(default_factory=CargasGlobales)
    combinaciones: list = field(default_factory=list)         # [str]
    edificios: list = field(default_factory=list)             # [Edificio]
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_proyecto_contenedores_y_orden_de_niveles -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): contenedores Proyecto/Edificio/Metadata/CargasGlobales"
```

---

## Task 5: Passing-through — `Edificio.niveles_atravesados` (regresión bug #2)

**Files:**
- Modify: `src/motor_fea/edificio/modelo.py` (método en `Edificio`)
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py  (añadir)
def test_columna_continua_atraviesa_los_tres_niveles():
    from motor_fea.edificio.modelo import Columna, Edificio, Nivel

    niveles = [Nivel(id=1, nombre="N1", cota=0.0),
               Nivel(id=2, nombre="N2", cota=3.0),
               Nivel(id=3, nombre="N3", cota=6.0)]
    col = Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=6.0, material="H210")
    edi = Edificio(id=1, nombre="Bloque A", niveles=niveles,
                   elementos_verticales=[col])

    atravesados = edi.niveles_atravesados(col)
    assert [n.cota for n in atravesados] == [0.0, 3.0, 6.0]   # conectada a los 3

    parcial = Columna(id=2, posicion=(1, 1), base=0.3, peralte=0.3,
                      cota_base=3.0, cota_tope=6.0, material="H210")
    assert [n.cota for n in edi.niveles_atravesados(parcial)] == [3.0, 6.0]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_columna_continua_atraviesa_los_tres_niveles -v`
Expected: FAIL con `AttributeError: 'Edificio' object has no attribute 'niveles_atravesados'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/modelo.py  (añadir dentro de class Edificio, tras cota_minima)
    def niveles_atravesados(self, vertical) -> list:
        """Niveles cuya cota cae en ``[cota_base, cota_tope]`` de la vertical.

        Base explícita para la futura bajada de cargas: una columna/muro continuo
        queda conectado a todos los niveles que atraviesa."""
        return [n for n in self.niveles_ordenados()
                if vertical.cota_base <= n.cota <= vertical.cota_tope]
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_columna_continua_atraviesa_los_tres_niveles -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): passing-through niveles_atravesados (bug #2)"
```

---

## Task 6: Validación parte 1 — IDs únicos + niveles

**Files:**
- Modify: `src/motor_fea/edificio/modelo.py` (añadir `Proyecto.validar`/`es_valido` + helper `_validar_niveles`)
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py  (añadir)
def test_validacion_niveles_e_ids():
    from motor_fea.edificio.modelo import Edificio, Nivel, Proyecto

    # Caso válido mínimo
    ok = Proyecto(edificios=[Edificio(id=1, nombre="A",
                                      niveles=[Nivel(1, "N1", 0.0)])])
    assert ok.validar() == []
    assert ok.es_valido() is True

    # Cotas duplicadas
    dup = Proyecto(edificios=[Edificio(id=1, nombre="A", niveles=[
        Nivel(1, "N1", 0.0), Nivel(2, "N2", 0.0)])])
    assert any("cota" in e.lower() for e in dup.validar())

    # Edificio sin niveles
    vacio = Proyecto(edificios=[Edificio(id=1, nombre="A", niveles=[])])
    assert any("al menos un nivel" in e.lower() for e in vacio.validar())

    # IDs de nivel duplicados
    ids = Proyecto(edificios=[Edificio(id=1, nombre="A", niveles=[
        Nivel(1, "N1", 0.0), Nivel(1, "N2", 3.0)])])
    assert any("id" in e.lower() and "nivel" in e.lower() for e in ids.validar())

    # IDs de edificio duplicados
    edis = Proyecto(edificios=[Edificio(id=1, nombre="A", niveles=[Nivel(1, "N", 0.0)]),
                               Edificio(id=1, nombre="B", niveles=[Nivel(1, "N", 0.0)])])
    assert any("edificio" in e.lower() for e in edis.validar())
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_validacion_niveles_e_ids -v`
Expected: FAIL con `AttributeError: 'Proyecto' object has no attribute 'validar'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/modelo.py  (añadir dentro de class Proyecto)
    def validar(self) -> list[str]:
        """Lista de errores legibles (vacía si el modelo es válido)."""
        errores: list[str] = []
        if len({e.id for e in self.edificios}) != len(self.edificios):
            errores.append("IDs de edificio duplicados.")
        for edi in self.edificios:
            errores.extend(_validar_niveles(edi))
        return errores

    def es_valido(self) -> bool:
        return not self.validar()
```

```python
# src/motor_fea/edificio/modelo.py  (añadir al final del módulo, nivel de módulo)
def _validar_niveles(edi: "Edificio") -> list[str]:
    errores: list[str] = []
    if not edi.niveles:
        errores.append(f"Edificio {edi.id}: debe tener al menos un nivel.")
        return errores
    if len({n.id for n in edi.niveles}) != len(edi.niveles):
        errores.append(f"Edificio {edi.id}: IDs de nivel duplicados.")
    cotas = [n.cota for n in edi.niveles]
    if len(set(cotas)) != len(cotas):
        errores.append(f"Edificio {edi.id}: cotas de nivel duplicadas "
                       "(deben ser estrictamente crecientes y únicas).")
    return errores
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py::test_validacion_niveles_e_ids -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): validación de niveles e IDs únicos"
```

---

## Task 7: Validación parte 2 — verticales + losas

**Files:**
- Modify: `src/motor_fea/edificio/modelo.py` (extender `validar` + helpers `_validar_verticales`, `_dimensiones_vertical`, `_validar_losas`)
- Test: `tests/test_edificio_modelo.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_modelo.py  (añadir)
def _edificio_base(verticales=(), losas=()):
    from motor_fea.edificio.modelo import Edificio, Nivel
    return Edificio(id=1, nombre="A",
                    niveles=[Nivel(1, "N1", 0.0, tuple(losas)),
                             Nivel(2, "N2", 3.0)],
                    elementos_verticales=list(verticales))


def test_validacion_verticales():
    from motor_fea.edificio.modelo import Columna, Proyecto

    # cota_base >= cota_tope
    mala = Columna(id=1, posicion=(0, 0), base=0.3, peralte=0.3,
                   cota_base=3.0, cota_tope=3.0, material="H210")
    assert any("cota_base" in e for e in Proyecto(edificios=[_edificio_base([mala])]).validar())

    # cota_tope no alineada con ningún nivel
    desalineada = Columna(id=2, posicion=(0, 0), base=0.3, peralte=0.3,
                          cota_base=0.0, cota_tope=2.5, material="H210")
    assert any("alinead" in e.lower() for e in Proyecto(edificios=[_edificio_base([desalineada])]).validar())

    # geometría no positiva
    sin_seccion = Columna(id=3, posicion=(0, 0), base=0.0, peralte=0.3,
                          cota_base=0.0, cota_tope=3.0, material="H210")
    assert any("positiv" in e.lower() for e in Proyecto(edificios=[_edificio_base([sin_seccion])]).validar())

    # válida: base = fundación (≤ cota mínima), tope alineado
    buena = Columna(id=4, posicion=(0, 0), base=0.3, peralte=0.3,
                    cota_base=-1.0, cota_tope=3.0, material="H210")
    assert Proyecto(edificios=[_edificio_base([buena])]).validar() == []


def test_validacion_losas():
    from motor_fea.edificio.modelo import Losa, Proyecto

    pocos = Losa(id=1, tipo="maciza", espesor=0.20, puntos=((0, 0), (1, 0)))
    assert any("punto" in e.lower() for e in Proyecto(edificios=[_edificio_base(losas=[pocos])]).validar())

    delgada = Losa(id=2, tipo="maciza", espesor=0.0, puntos=((0, 0), (1, 0), (1, 1)))
    assert any("espesor" in e.lower() for e in Proyecto(edificios=[_edificio_base(losas=[delgada])]).validar())

    rara = Losa(id=3, tipo="inventada", espesor=0.20, puntos=((0, 0), (1, 0), (1, 1)))
    assert any("tipo" in e.lower() for e in Proyecto(edificios=[_edificio_base(losas=[rara])]).validar())
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py -k "validacion_verticales or validacion_losas" -v`
Expected: FAIL (los errores esperados no se reportan todavía)

- [ ] **Step 3: Write minimal implementation**

Reemplazar `Proyecto.validar` por esta versión (extiende la de Task 6 con verticales y losas):

```python
# src/motor_fea/edificio/modelo.py  (reemplazar el cuerpo de Proyecto.validar)
    def validar(self) -> list[str]:
        """Lista de errores legibles (vacía si el modelo es válido)."""
        errores: list[str] = []
        if len({e.id for e in self.edificios}) != len(self.edificios):
            errores.append("IDs de edificio duplicados.")
        for edi in self.edificios:
            errores.extend(_validar_niveles(edi))
            errores.extend(_validar_verticales(edi))
            errores.extend(_validar_losas(edi))
        return errores
```

Añadir los helpers al final del módulo (tras `_validar_niveles`):

```python
# src/motor_fea/edificio/modelo.py  (añadir tras _validar_niveles)
def _validar_verticales(edi: "Edificio") -> list[str]:
    errores: list[str] = []
    cotas_nivel = {n.cota for n in edi.niveles}
    cota_min = edi.cota_minima()
    if len({v.id for v in edi.elementos_verticales}) != len(edi.elementos_verticales):
        errores.append(f"Edificio {edi.id}: IDs de vertical duplicados.")
    for v in edi.elementos_verticales:
        et = f"Edificio {edi.id} vertical {v.id}"
        if v.cota_base >= v.cota_tope:
            errores.append(f"{et}: cota_base ({v.cota_base}) debe ser menor que cota_tope ({v.cota_tope}).")
        if v.cota_tope not in cotas_nivel:
            errores.append(f"{et}: cota_tope ({v.cota_tope}) no alineada con ningún nivel.")
        # La base puede ser fundación (≤ cota mínima); si no, debe alinear con un nivel.
        if v.cota_base not in cotas_nivel and v.cota_base > cota_min:
            errores.append(f"{et}: cota_base ({v.cota_base}) no alineada con ningún nivel ni con la fundación.")
        for valor, etiq in _dimensiones_vertical(v):
            if valor <= 0:
                errores.append(f"{et}: {etiq} debe ser positivo.")
        if v.zapata is not None:
            for etiq, valor in (("ancho", v.zapata.ancho), ("largo", v.zapata.largo),
                                ("peralte", v.zapata.peralte)):
                if valor <= 0:
                    errores.append(f"{et}: zapata.{etiq} debe ser positivo.")
    return errores


def _dimensiones_vertical(v) -> list:
    """[(valor, etiqueta), ...] de las dimensiones de sección de una vertical."""
    if isinstance(v, Columna):
        return [(v.base, "base"), (v.peralte, "peralte")]
    if isinstance(v, Muro):
        return [(v.espesor, "espesor")]
    return []


def _validar_losas(edi: "Edificio") -> list[str]:
    errores: list[str] = []
    for nivel in edi.niveles:
        if len({l.id for l in nivel.losas}) != len(nivel.losas):
            errores.append(f"Edificio {edi.id} nivel {nivel.id}: IDs de losa duplicados.")
        for l in nivel.losas:
            et = f"Edificio {edi.id} nivel {nivel.id} losa {l.id}"
            if len(l.puntos) < 3:
                errores.append(f"{et}: el contorno necesita al menos 3 puntos.")
            if l.espesor <= 0:
                errores.append(f"{et}: espesor debe ser positivo.")
            if l.tipo not in TIPOS_LOSA:
                errores.append(f"{et}: tipo '{l.tipo}' fuera del catálogo {sorted(TIPOS_LOSA)}.")
    return errores
```

> Nota: `_dimensiones_vertical` usa `isinstance(v, Columna)`/`Muro`; ambas clases ya están definidas arriba en el mismo módulo, así que las referencias resuelven en tiempo de llamada.

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_modelo.py -v`
Expected: PASS (todos los tests del módulo)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/modelo.py tests/test_edificio_modelo.py
git commit -m "feat(A): validación de verticales (alineación/geometría) y losas"
```

---

## Task 8: Contrato JSON versionado — dict ↔ Proyecto

**Files:**
- Create: `src/motor_fea/edificio/contrato.py`
- Modify: `src/motor_fea/edificio/__init__.py` (re-exports públicos)
- Test: `tests/test_edificio_contrato.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_contrato.py
"""Tests del contrato JSON del modelo canónico (Rebanada A)."""
import pytest

from motor_fea.edificio.contrato import (
    VERSION_CONTRATO, proyecto_a_dict, proyecto_desde_dict,
)
from motor_fea.edificio.modelo import (
    CargasGlobales, CargasLosa, Columna, Edificio, Losa, Metadata, Muro,
    Nivel, Proyecto, Zapata,
)


def _proyecto_demo() -> Proyecto:
    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)),
                cargas=CargasLosa(muerta=1.5, viva=2.0))
    edi = Edificio(
        id=1, nombre="Bloque A",
        niveles=[Nivel(1, "Primer nivel", 0.0, (losa,)),
                 Nivel(2, "Segundo nivel", 3.0)],
        elementos_verticales=[
            Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                    cota_base=0.0, cota_tope=3.0, material="H210",
                    zapata=Zapata(1.2, 1.2, 0.4)),
            Muro(id=2, linea=((0, 0), (0, 5)), espesor=0.20,
                 cota_base=0.0, cota_tope=3.0, material="H210"),
        ])
    return Proyecto(metadata=Metadata(nombre="Edificio demo", fecha="2026-06-15"),
                    cargas_globales=CargasGlobales(1.5, 2.0),
                    combinaciones=["1.2D+1.6L"], edificios=[edi])


def test_round_trip_parse_serialize_parse():
    proy = _proyecto_demo()
    d = proyecto_a_dict(proy)
    assert d["version"] == VERSION_CONTRATO
    reconstruido = proyecto_desde_dict(d)
    assert reconstruido == proy                      # round-trip exacto
    assert proyecto_a_dict(reconstruido) == d        # estable


def test_version_no_soportada_falla():
    d = proyecto_a_dict(_proyecto_demo())
    d["version"] = 99
    with pytest.raises(ValueError, match="ersión"):
        proyecto_desde_dict(d)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_edificio_contrato.py -v`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.edificio.contrato'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/contrato.py
"""(De)serialización JSON del modelo canónico del edificio (Rebanada A).

Única capa con I/O del paquete ``edificio``. Contrato versionado: ``version`` en
la raíz. Round-trip exacto ``parse → serialize → parse``.
"""
from __future__ import annotations

import json

from motor_fea.edificio.modelo import (
    CargasGlobales,
    CargasLosa,
    Columna,
    Edificio,
    Losa,
    Metadata,
    Muro,
    Nivel,
    Proyecto,
    Zapata,
)

VERSION_CONTRATO = 1


# --------------------------- parse (dict → objetos) ---------------------------
def _zapata_desde(d: dict | None) -> Zapata | None:
    if not d:
        return None
    return Zapata(float(d["ancho"]), float(d["largo"]), float(d["peralte"]))


def _vertical_desde(d: dict):
    tipo = d.get("tipo")
    if tipo == "columna":
        x, y = d["posicion"]
        return Columna(int(d["id"]), (float(x), float(y)),
                       float(d["seccion"]["base"]), float(d["seccion"]["peralte"]),
                       float(d["cota_base"]), float(d["cota_tope"]), str(d["material"]),
                       _zapata_desde(d.get("zapata")))
    if tipo == "muro":
        (x1, y1), (x2, y2) = d["linea"]
        return Muro(int(d["id"]), ((float(x1), float(y1)), (float(x2), float(y2))),
                    float(d["seccion"]["espesor"]),
                    float(d["cota_base"]), float(d["cota_tope"]), str(d["material"]),
                    _zapata_desde(d.get("zapata")))
    raise ValueError(f"Tipo de elemento vertical desconocido: {tipo!r}.")


def _losa_desde(d: dict) -> Losa:
    c = d.get("cargas", {})
    return Losa(int(d["id"]), str(d["tipo"]), float(d["espesor"]),
                tuple((float(p[0]), float(p[1])) for p in d["puntos"]),
                CargasLosa(float(c.get("muerta", 0.0)), float(c.get("viva", 0.0))))


def _nivel_desde(d: dict) -> Nivel:
    return Nivel(int(d["id"]), str(d["nombre"]), float(d["cota"]),
                 tuple(_losa_desde(l) for l in d.get("losas", [])))


def _edificio_desde(d: dict) -> Edificio:
    return Edificio(int(d["id"]), str(d["nombre"]),
                    [_nivel_desde(n) for n in d.get("niveles", [])],
                    [_vertical_desde(v) for v in d.get("elementos_verticales", [])])


def proyecto_desde_dict(d: dict) -> Proyecto:
    """Construye un :class:`Proyecto` desde un dict (JSON ya parseado)."""
    v = d.get("version")
    if v != VERSION_CONTRATO:
        raise ValueError(f"Versión de contrato no soportada: {v!r} (esperada {VERSION_CONTRATO}).")
    p = d.get("proyecto", {})
    cg = d.get("cargas_globales", {})
    return Proyecto(
        metadata=Metadata(str(p.get("nombre", "")), str(p.get("autor", "")),
                          str(p.get("codigo_obra", "")), str(p.get("ubicacion", "")),
                          str(p.get("fecha", ""))),
        cargas_globales=CargasGlobales(float(cg.get("muerta_adicional", 0.0)),
                                       float(cg.get("viva", 0.0))),
        combinaciones=[str(c) for c in d.get("combinaciones", [])],
        edificios=[_edificio_desde(e) for e in d.get("edificios", [])],
    )


# ------------------------- serialize (objetos → dict) -------------------------
def _zapata_a(z: Zapata | None) -> dict | None:
    if z is None:
        return None
    return {"ancho": z.ancho, "largo": z.largo, "peralte": z.peralte}


def _vertical_a(v) -> dict:
    base = {"id": v.id, "cota_base": v.cota_base, "cota_tope": v.cota_tope,
            "material": v.material}
    if isinstance(v, Columna):
        base.update({"tipo": "columna", "posicion": list(v.posicion),
                     "seccion": {"base": v.base, "peralte": v.peralte}})
    else:  # Muro
        base.update({"tipo": "muro", "linea": [list(p) for p in v.linea],
                     "seccion": {"espesor": v.espesor}})
    z = _zapata_a(v.zapata)
    if z is not None:
        base["zapata"] = z
    return base


def _losa_a(l: Losa) -> dict:
    return {"id": l.id, "tipo": l.tipo, "espesor": l.espesor,
            "puntos": [list(p) for p in l.puntos],
            "cargas": {"muerta": l.cargas.muerta, "viva": l.cargas.viva}}


def _nivel_a(n: Nivel) -> dict:
    return {"id": n.id, "nombre": n.nombre, "cota": n.cota,
            "losas": [_losa_a(l) for l in n.losas]}


def _edificio_a(e: Edificio) -> dict:
    return {"id": e.id, "nombre": e.nombre,
            "niveles": [_nivel_a(n) for n in e.niveles],
            "elementos_verticales": [_vertical_a(v) for v in e.elementos_verticales]}


def proyecto_a_dict(p: Proyecto) -> dict:
    """Serializa un :class:`Proyecto` a un dict JSON-able versionado (round-trip exacto)."""
    m = p.metadata
    return {
        "version": VERSION_CONTRATO,
        "proyecto": {"nombre": m.nombre, "autor": m.autor, "codigo_obra": m.codigo_obra,
                     "ubicacion": m.ubicacion, "fecha": m.fecha},
        "cargas_globales": {"muerta_adicional": p.cargas_globales.muerta_adicional,
                            "viva": p.cargas_globales.viva},
        "combinaciones": list(p.combinaciones),
        "edificios": [_edificio_a(e) for e in p.edificios],
    }


def proyecto_desde_json(texto: str) -> Proyecto:
    """JSON (texto) → :class:`Proyecto`."""
    return proyecto_desde_dict(json.loads(texto))


def proyecto_a_json(p: Proyecto) -> str:
    """:class:`Proyecto` → JSON (texto indentado)."""
    return json.dumps(proyecto_a_dict(p), indent=2, ensure_ascii=False)
```

Actualizar el `__init__.py` con re-exports públicos:

```python
# src/motor_fea/edificio/__init__.py  (añadir al final)
from motor_fea.edificio.contrato import (  # noqa: E402
    VERSION_CONTRATO,
    proyecto_a_dict,
    proyecto_a_json,
    proyecto_desde_dict,
    proyecto_desde_json,
)
from motor_fea.edificio.modelo import (  # noqa: E402
    CargasGlobales,
    CargasLosa,
    Columna,
    Edificio,
    Losa,
    Metadata,
    Muro,
    Nivel,
    Proyecto,
    TIPOS_LOSA,
    Zapata,
)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_contrato.py -v`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/contrato.py src/motor_fea/edificio/__init__.py tests/test_edificio_contrato.py
git commit -m "feat(A): contrato JSON versionado proyecto↔dict + round-trip"
```

---

## Task 9: Escenarios del bug end-to-end por el contrato + suite completa

**Files:**
- Modify: `tests/test_edificio_contrato.py` (escenarios end-to-end)
- Test: toda la suite

- [ ] **Step 1: Write the failing test**

```python
# tests/test_edificio_contrato.py  (añadir)
def test_bug_end_to_end_por_el_contrato():
    """Los 3 escenarios del bug, parseados desde el JSON del contrato."""
    from motor_fea.edificio.contrato import proyecto_desde_dict

    d = {
        "version": 1,
        "proyecto": {"nombre": "Edificio demo", "fecha": "2026-06-15"},
        "cargas_globales": {"muerta_adicional": 1.5, "viva": 2.0},
        "combinaciones": ["1.2D+1.6L"],
        "edificios": [{
            "id": 1, "nombre": "Bloque A",
            "niveles": [
                {"id": 1, "nombre": "Primer nivel", "cota": 0.0,
                 "losas": [{"id": 1, "tipo": "maciza", "espesor": 0.20,
                            "puntos": [[0, 0], [5, 0], [5, 5], [0, 5]],
                            "cargas": {"muerta": 1.5, "viva": 2.0}}]},
                {"id": 2, "nombre": "Segundo nivel", "cota": 3.0, "losas": []},
                {"id": 3, "nombre": "Tercer nivel", "cota": 6.0, "losas": []},
            ],
            "elementos_verticales": [
                {"id": 1, "tipo": "columna", "posicion": [0, 0],
                 "seccion": {"base": 0.30, "peralte": 0.30},
                 "cota_base": 0.0, "cota_tope": 6.0, "material": "H210",
                 "zapata": {"ancho": 1.2, "largo": 1.2, "peralte": 0.4}},
            ],
        }],
    }
    proy = proyecto_desde_dict(d)
    assert proy.validar() == []                       # modelo válido

    edi = proy.edificios[0]
    nivel1 = edi.niveles[0]
    losa1 = nivel1.losas[0]

    # #1 — la cota del nivel se propaga a la losa
    assert all(z == 0.0 for (_x, _y, z) in nivel1.puntos_losa_3d(losa1))
    # #1 — el nombre del nivel es independiente del de la losa
    assert nivel1.nombre == "Primer nivel"
    # #2 — la columna continua 0→6 queda conectada a los 3 niveles
    col = edi.elementos_verticales[0]
    assert [n.cota for n in edi.niveles_atravesados(col)] == [0.0, 3.0, 6.0]
```

- [ ] **Step 2: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_edificio_contrato.py::test_bug_end_to_end_por_el_contrato -v`
Expected: PASS (la funcionalidad ya existe; este test la fija como no-regresión a través del contrato). Si fallara, corregir antes de continuar.

- [ ] **Step 3: Run the full suite**

Run: `.venv/bin/pytest -q`
Expected: PASS — 228 tests previos + los nuevos del paquete `edificio`, todos verdes.

- [ ] **Step 4: Verificar ausencia de `losasplus` en el código nuevo (D5)**

Run: `grep -ri "losasplus" src/motor_fea/edificio/ tests/test_edificio_modelo.py tests/test_edificio_contrato.py`
Expected: sin coincidencias (exit code 1 / salida vacía).

- [ ] **Step 5: Commit**

```bash
git add tests/test_edificio_contrato.py
git commit -m "test(A): escenarios del bug end-to-end por el contrato; suite verde"
```

---

## Self-review (cobertura de la spec)

| Requisito de la spec | Task |
|---|---|
| D1 Nivel=Sistema con cota única | Task 2 (`Nivel.cota`, sin cota en losa) |
| D2 Verticales continuas (`cota_base→cota_tope`) | Task 3 (`Columna`/`Muro`) |
| D3 Contrato versionado dueño del motor | Task 8 (`VERSION_CONTRATO`, raíz `version`) |
| D4 Sin importador | No se implementa importador (ningún task lo crea) |
| D5 Naming EstructurasRD, cero `losasplus` | Task 9 step 4 (grep guard) |
| Modelo canónico (jerarquía completa) | Tasks 1–4 |
| Implementación de referencia (dataclasses + parseo/serialización) | Tasks 1–4 (modelo) + Task 8 (contrato) |
| Validación con reportes legibles | Tasks 6–7 (`validar() -> list[str]`) |
| Passing-through computado | Task 5 (`niveles_atravesados`) |
| Round-trip parse→serialize→parse | Task 8 |
| Casos de validación (cotas, cota_base≥tope, fuera de niveles, losa <3 puntos / espesor≤0, IDs duplicados) | Tasks 6–7 |
| Regresión: cota se propaga a losas | Tasks 2 y 9 |
| Regresión: columna 0→6 conectada a 3 niveles | Tasks 5 y 9 |
| Regresión: nombre de nivel independiente de la losa | Tasks 2 y 9 |

**Follow-ups registrados (fuera de A):** síntesis FEA (modelo de autoría → nodos/barras con nodos compartidos), bajada de cargas (B), rebrand `losasplus` del resto del código. No entran en este plan.

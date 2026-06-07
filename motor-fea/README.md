# motor-fea

Motor de análisis y diseño estructural **nativo** (FEA) de EstructurasRD —
backend de cálculo destinado a reemplazar la dependencia del `Losas.exe` externo
en LosasPlus. Ver `../PLAN_MAESTRO.md` (Track B) y `docs/ADR-0001-integracion.md`.

Implementado en **Python puro** (sin NumPy en runtime) para correr en cualquier
intérprete; todo validado contra **soluciones cerradas exactas**. NumPy/SciPy
quedan declaradas para acelerar a escala urbana (B5).

## Capas (no se mezclan)

| Capa | Paquete | Responsabilidad |
|---|---|---|
| 1 Análisis | `motor_fea.core` | rigidez directa, frame 3D 12 GDL, modal |
| 2 Normativa | `motor_fea.normativa` | code-checking ACI 318-19 + MOPC R-001 |
| 3 Frontera | `motor_fea.api` | CLI/HTTP, (de)serialización JSON |
| Composición | `motor_fea.sismo` | casos de uso que orquestan 1+2 |

## Capacidades

**Análisis (`core`)**
- `solver.resolver` — estático lineal de marcos 3D (elemento frame 12 GDL: axial,
  torsión, flexión en 2 planos), desplazamientos, reacciones. Validado vs
  `PL/AE`, `PL³/3EI`, `TL/GJ`, `ML/EI`.
- `modal.periodo_fundamental` / `modal.modos(n)` — análisis modal con masas
  nodales (condensación Guyan + iteración inversa con deflación M-ortogonal),
  `modal.participacion_modal` (% de masa). Validado vs `3EI/L³/m` y cadena 2 GDL
  `(k/m)(3∓√5)/2`.

**Normativa (`normativa`)**
- `aci318` — viga (flexión + cortante), columna (P-M por compatibilidad de
  deformaciones), zapata (punzonamiento §22.6), losa a flexión (As mín. temp.
  §24.4.3.2). Unidades SI (N·mm·MPa).
- `combinaciones` — combos LRFD §5.3.1 (D/L/Lr/S/R/W/E, W/E reversibles) + envolvente.
- `r001` — zonas sísmicas RD I/II, espectro de diseño, cortante basal Cb.

**Composición**
- `sismo.cortante_basal_sismico` — modelo + masas → T (modal) → Sa (R-001) → V=Cb·W.

**Frontera (`api`)**
- `motor-fea --analyze modelo.json` → resultados JSON (o `-` para stdin).
- `motor-fea --serve [modelo.json]` → visor 3D WebXR (VR + móvil) en
  `http://<host>:8000/`. Requiere el extra `api` (`pip install -e ".[api]"`).
  Sin `modelo.json` sirve un pórtico de ejemplo. Usa `--host 0.0.0.0` para
  acceder desde el celular/Quest en la misma red.

Además de la geometría, el visor obtiene `GET /resultados` y ofrece un panel
(arriba a la derecha) para alternar entre **sin deformar**, la **deformada** bajo
peso propio + cargas, y los **modos 1–3**. Un slider exagera el desplazamiento y
se muestra el período real `T` de cada modo; el cálculo ocurre en el servidor
(reusa el solver y el análisis modal) y el visor solo anima en el cliente.

La vista también obtiene `GET /losa`: el selector gana estados **losa: deflexión /
momento Mx / momento My** que muestran una losa como superficie coloreada (mapa de
calor) y abombada según su deformada. Tocar un punto de la losa muestra el valor
interpolado del campo activo (deflexión en mm, momentos en kN·m/m). El FEM de la
losa corre en el servidor (reusa `losa_fem`); el visor solo colorea y anima.

Y `GET /armado`: el estado **refuerzo: armado** muestra, dentro de cada sección,
el armado de ejemplo en 3D — barras longitudinales (cilindros) y estribos (aros)
con el hormigón semi-transparente. La cantidad de acero sale de reglas ACI mínimas
(ρ≈1% en columnas, As mínimo a flexión en vigas) reusando `aci318`; el motor Python
no calcula esfuerzos por elemento, así que es un armado representativo, no un diseño
por carga.

Y `GET /diseno`: el estado **diseño: armado** muestra el armado **diseñado por las fuerzas
reales** del análisis (reusa `esfuerzos_elementos` + el diseño ACI por elemento), coloreado
por si cumple (gris) o no (rojo) la demanda. Tocar un elemento muestra su designación y su
demanda (columna: Pu/Mu; viga: Mu/Vu). A diferencia de `refuerzo` (armado de ejemplo), acá el
acero sale del cálculo por carga.

Las columnas traen además su **estribo diseñado** (cortante con axial ACI §22.5.6.1 + confinamiento
sísmico §18.7.5.4, escalando la barra del estribo hasta confinar); la jaula del visor dibuja la
separación real y la etiqueta la muestra (`E#4 2R @ 50`).

## Desarrollo

```bash
cd motor-fea
python3 -m venv .venv && . .venv/bin/activate
pip install -e ".[dev]"        # numpy/scipy/pytest
PYTHONPATH=src pytest -q       # 108 tests (sin numpy: la suite es stdlib pura)
```

## Estado (ver PLAN_MAESTRO.md Track B)

- ✅ B0 scaffolding · B1 solver frame 3D · B1.5 contrato JSON+CLI · B2 modal
  (multi-modo + participación + SRSS/CQC + modal-espectral) · B3 ACI 318-19
  (viga/columna/zapata/losa/combos) · sismo R-001 · **shells: losas por FEM
  (elemento ACM, análisis→momentos→diseño vano+apoyo→CLI, validado por
  convergencia vs Timoshenko, cuadrada/rectangular/empotrada)** · **diafragma
  rígido**. Capstone: flujo de edificio (pórtico+diafragma+modal+sismo+diseño).
- ⏳ MITC4 (placas gruesas, nicho), BIM (IfcOpenShell), escala urbana
  (PostGIS/3D Tiles, Rust), **puente C# ↔ motor (B6)**.

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

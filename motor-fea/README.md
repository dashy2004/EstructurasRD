# motor-fea

Motor de análisis y diseño estructural **nativo** (FEA) de EstructurasRD —
backend de cálculo destinado a reemplazar la dependencia del `Losas.exe` externo
en LosasPlus. Ver `../PLAN_MAESTRO.md` (Track B) y `docs/ADR-0001-integracion.md`.

## Capas

- `src/motor_fea/core/` — análisis FEA puro (rigidez directa, frame 3D 12 GDL,
  shells, modal). Usa NumPy/SciPy.
- `src/motor_fea/normativa/` — code-checking (ACI 318-19, MOPC R-001/R-033 →
  CDCRD/ASCE 7-22). Sólo stdlib.
- `src/motor_fea/api/` — frontera CLI/HTTP que el C# invoca.

## Desarrollo

```bash
cd motor-fea
python3 -m venv .venv && . .venv/bin/activate
pip install -e ".[dev]"        # numpy/scipy/pytest
pytest                         # smoke-tests (B0) + solver (B1+)
```

Los smoke-tests de B0 corren **sólo con stdlib** (sin instalar nada):

```bash
PYTHONPATH=src python3 -m pytest tests/ -q     # si pytest está disponible
# o, sin pytest:
PYTHONPATH=src python3 -c "import tests.test_smoke as t; [getattr(t,n)() for n in dir(t) if n.startswith('test_')]; print('OK')"
```

## Estado

- **B0** ✅ scaffolding + dominio + constantes R-001 + CLI `--version` + smoke-tests.
- **B1** ⏳ solver de rigidez directa frame 3D, validado vs PyNite (<1% error).

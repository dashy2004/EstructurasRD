"""motor-fea — motor de análisis y diseño estructural nativo de EstructurasRD.

Capas (no se mezclan):
  core/       análisis FEA puro (rigidez directa, frame 3D, shells, modal).
  normativa/  code-checking (ACI 318-19, MOPC R-001/R-033 → CDCRD/ASCE 7-22).
  api/        frontera de integración (CLI / HTTP) que el C# invoca.
"""
__version__ = "0.0.1"

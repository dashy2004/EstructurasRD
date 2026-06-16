"""Síntesis FEA: modelo de autoría (Edificio) → malla estructural (ModeloEstructural).

Rebanada B0 — solo columnas: nodos compartidos por coordenada, barras entre
quiebres, zapata→apoyo, material/sección desde autoría, losas como geometría
inerte para el visor. Sin I/O, sin NumPy. Asume un edificio válido; garantiza
una salida que pasa ``ModeloEstructural.validar()``.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import (
    Apoyo,
    ElementoFrame,
    LosaViz,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.edificio.modelo import Columna, Edificio

_TOL = 6  # decimales de cuantización de coordenadas (≈ mm)

FACTOR_E_ACI = 15100.0     # E[kg/cm²] = 15100·√(f'c[kg/cm²])  (ACI 318, concreto)
KGF_CM2_A_PA = 98066.5     # 1 kgf/cm² en pascales


def material_a_E_pa(material: str) -> float:
    """Convierte un material de obra ``'H{n}'`` (f'c en kg/cm²) a E en pascales (ACI)."""
    s = material.strip().upper()
    fc = None
    if s.startswith("H"):
        try:
            fc = float(s[1:])
        except ValueError:
            fc = None
    if fc is None:
        raise ValueError(f"Material no reconocido: {material!r} (se espera 'H<f'c en kg/cm²>').")
    if fc <= 0:
        raise ValueError(f"Material {material!r}: f'c debe ser positivo.")
    return FACTOR_E_ACI * math.sqrt(fc) * KGF_CM2_A_PA

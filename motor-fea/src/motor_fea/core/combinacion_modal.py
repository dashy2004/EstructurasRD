"""Combinación de respuestas modales — SRSS y CQC (análisis espectral).

Capa 1 (análisis). Al hacer análisis modal-espectral, cada modo aporta una
respuesta máxima (cortante, desplazamiento, momento…); como los máximos no
ocurren a la vez, se combinan estadísticamente:

- **SRSS** (raíz de la suma de cuadrados): supone modos **no correlacionados**;
  válido sólo con frecuencias bien separadas.
- **CQC** (combinación cuadrática completa, Der Kiureghian 1981): incluye la
  correlación ρᵢⱼ entre modos; se reduce a SRSS para modos separados y crece
  cuando hay frecuencias cercanas (p. ej. torsión). R-001 / ASCE 7 admiten CQC.

Funciones puras (sin NumPy).
"""
from __future__ import annotations

import math


def srss(valores: list[float]) -> float:
    """Raíz de la suma de cuadrados de las respuestas modales."""
    return math.sqrt(sum(v * v for v in valores))


def coeficiente_correlacion(wi: float, wj: float, zeta: float = 0.05) -> float:
    """Coeficiente de correlación ρᵢⱼ de CQC (amortiguamiento igual ζ), Der Kiureghian.

    ρ = 1 si los modos coinciden; → 0 cuando las frecuencias se separan.
    """
    if wi <= 0 or wj <= 0:
        raise ValueError("Las frecuencias deben ser positivas.")
    r = wi / wj
    if abs(r - 1.0) < 1e-15:
        return 1.0
    num = 8.0 * zeta * zeta * (1.0 + r) * r ** 1.5
    den = (1.0 - r * r) ** 2 + 4.0 * zeta * zeta * r * (1.0 + r) ** 2
    return num / den


def cqc(valores: list[float], frecuencias: list[float], zeta: float = 0.05) -> float:
    """Combinación cuadrática completa: √(ΣᵢΣⱼ ρᵢⱼ vᵢ vⱼ)."""
    if len(valores) != len(frecuencias):
        raise ValueError("valores y frecuencias deben tener la misma longitud.")
    n = len(valores)
    total = 0.0
    for i in range(n):
        for j in range(n):
            rho = 1.0 if i == j else coeficiente_correlacion(frecuencias[i], frecuencias[j], zeta)
            total += rho * valores[i] * valores[j]
    return math.sqrt(max(total, 0.0))

"""MOPC R-001 (Decreto 201-11) — parámetros sísmicos de la República Dominicana.

Capa 2 (code-checking), separada del análisis. Codifica las constantes del
Reglamento para el Análisis y Diseño Sísmico de Estructuras vigente; está
diseñada para versionarse cuando el nuevo CDCRD (Resolución MIVHED 007-2026,
obligatorio desde 2027-04-10, adopta ASCE/SEI 7-22) entre en vigor.

Valores de referencia (TABLA 1, período de retorno 2,475 años):
  Zona I  (alta sismicidad):  Ss = 1.55 g, S1 = 0.75 g
  Zona II (mediana):          Ss = 0.95 g, S1 = 0.55 g
con Ss para T=0.20 s y S1 para T=1.00 s.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class ZonaSismica(Enum):
    """Zonificación sísmica de R-001."""
    ZONA_I = "I"    # alta sismicidad
    ZONA_II = "II"  # mediana sismicidad


@dataclass(frozen=True)
class ParametrosZona:
    """Aceleraciones espectrales de referencia (en g)."""
    ss: float  # T = 0.20 s
    s1: float  # T = 1.00 s


# TABLA 1 de R-001 — aceleraciones espectrales de referencia por zona (g).
PARAMETROS_ZONA: dict[ZonaSismica, ParametrosZona] = {
    ZonaSismica.ZONA_I: ParametrosZona(ss=1.55, s1=0.75),
    ZonaSismica.ZONA_II: ParametrosZona(ss=0.95, s1=0.55),
}

# Cortante basal mínimo (R-001): Cb >= 0.03.
CORTANTE_BASAL_MINIMO = 0.03


def espectro_diseno(zona: ZonaSismica, fa: float, fv: float) -> dict[str, float]:
    """Parámetros del espectro de diseño de R-001 (amortiguamiento 5%).

    SDS = (2/3)·Fa·Ss ; SD1 = (2/3)·Fv·S1 ; T0 = 0.2·(SD1/SDS) ; Ts = 5·T0.

    Args:
        zona: zona sísmica (I o II).
        fa: factor de sitio para períodos cortos.
        fv: factor de sitio para períodos largos.

    Returns:
        dict con SDS, SD1, T0, Ts.
    """
    p = PARAMETROS_ZONA[zona]
    sds = (2.0 / 3.0) * fa * p.ss
    sd1 = (2.0 / 3.0) * fv * p.s1
    t0 = 0.2 * (sd1 / sds) if sds > 0 else 0.0
    ts = 5.0 * t0
    return {"SDS": sds, "SD1": sd1, "T0": t0, "Ts": ts}


def cortante_basal(u: float, sa: float, rd: float) -> float:
    """Coeficiente de cortante basal Cb = (U·Sa)/Rd, con piso de 0.03 (R-001).

    Args:
        u: factor de importancia (Grupo I=1.50 … V=0.90).
        sa: aceleración espectral de diseño en el período de la estructura (g).
        rd: factor de reducción de respuesta sísmica (TABLA 8).
    """
    if rd <= 0:
        raise ValueError("Rd debe ser positivo.")
    return max((u * sa) / rd, CORTANTE_BASAL_MINIMO)

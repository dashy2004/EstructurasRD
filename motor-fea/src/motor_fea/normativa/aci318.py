"""ACI 318-19 — verificación de vigas de hormigón armado (flexión y cortante).

Capa 2 (code-checking), separada del análisis. Funciones puras que devuelven
áreas de acero, resistencias y ratios demanda/capacidad.

**Unidades: SI estructural — N, mm, MPa** (la forma literal de ACI 318-19 SI).
La conversión desde las unidades de obra (kg/cm², ton·m) vive en la frontera,
no acá. Recordatorios: f'c, fy en MPa; b, d, s en mm; Mu en N·mm; Vu en N;
As, Av en mm².

Referencias: ACI 318-19 §22.2 (flexión), §9.6.1.2 (As mínimo), §22.5 (cortante).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

PHI_FLEXION = 0.90      # controlado por tracción, Tabla 21.2.1
PHI_CORTANTE = 0.75     # cortante, Tabla 21.2.1


# ----------------------------- Flexión -----------------------------
def as_requerido_flexion(mu: float, b: float, d: float, fc: float, fy: float,
                         phi: float = PHI_FLEXION) -> tuple[float, bool]:
    """As requerido (mm²) para un Mu (N·mm) en sección rectangular simplemente armada.

    Devuelve ``(As, seccion_insuficiente)``; As = NaN si no alcanza como
    simplemente armada (radicando negativo → requiere compresión o más peralte).
    """
    mu = abs(mu)
    if mu == 0:
        return 0.0, False
    if d <= 0 or fc <= 0 or fy <= 0 or b <= 0:
        raise ValueError("b, d, f'c y fy deben ser positivos.")
    k = 0.85 * fc * b                       # N/mm
    rad = d * d - 2.0 * mu / (phi * k)
    if rad < 0:
        return float("nan"), True
    return (k / fy) * (d - math.sqrt(rad)), False


def as_minimo_flexion(b: float, d: float, fc: float, fy: float) -> float:
    """As mínimo a flexión (mm²), ACI 318-19 §9.6.1.2: max(0.25√f'c/fy, 1.4/fy)·b·d."""
    rho = max(0.25 * math.sqrt(fc) / fy, 1.4 / fy)
    return rho * b * d


def momento_resistente(as_: float, b: float, d: float, fc: float, fy: float,
                       phi: float = PHI_FLEXION) -> float:
    """φMn (N·mm) de una sección rectangular con As (mm²) dado."""
    a = as_ * fy / (0.85 * fc * b)
    return phi * as_ * fy * (d - a / 2.0)


@dataclass(frozen=True)
class ResultadoFlexion:
    as_requerido: float       # mm²
    as_minimo: float          # mm²
    as_diseno: float          # mm² = max(req, min)
    seccion_insuficiente: bool
    phi_mn: float | None      # φMn con As provisto (N·mm), si se dio As provisto
    ratio: float | None       # Mu / φMn (≤1 cumple)
    cumple: bool


def verificar_viga_flexion(mu: float, b: float, d: float, fc: float, fy: float,
                           as_provisto: float | None = None) -> ResultadoFlexion:
    """Verifica una viga a flexión: As req. vs mín., y (si hay As provisto) el ratio Mu/φMn."""
    as_req, insuf = as_requerido_flexion(mu, b, d, fc, fy)
    as_min = as_minimo_flexion(b, d, fc, fy)
    as_dis = float("nan") if insuf else max(as_req, as_min)

    phi_mn = ratio = None
    cumple = not insuf
    if as_provisto is not None and not insuf:
        phi_mn = momento_resistente(as_provisto, b, d, fc, fy)
        ratio = abs(mu) / phi_mn if phi_mn > 0 else float("inf")
        cumple = ratio <= 1.0 + 1e-9 and as_provisto >= as_dis - 1e-6
    return ResultadoFlexion(as_req, as_min, as_dis, insuf, phi_mn, ratio, cumple)


# ----------------------------- Cortante -----------------------------
def cortante_concreto(bw: float, d: float, fc: float, lam: float = 1.0) -> float:
    """Vc (N) — aporte del concreto, ACI 318-19 §22.5.5.1 simplificado: 0.17·λ·√f'c·bw·d."""
    return 0.17 * lam * math.sqrt(fc) * bw * d


def cortante_acero(av: float, fyt: float, d: float, s: float) -> float:
    """Vs (N) — aporte de los estribos: Av·fyt·d/s (ACI 318-19 §22.5.10.5.3)."""
    if s <= 0:
        raise ValueError("La separación de estribos s debe ser positiva.")
    return av * fyt * d / s


def cortante_acero_maximo(bw: float, d: float, fc: float) -> float:
    """Vs máximo permitido (N), ACI 318-19 §22.5.1.2: 0.66·√f'c·bw·d."""
    return 0.66 * math.sqrt(fc) * bw * d


@dataclass(frozen=True)
class ResultadoCortante:
    vc: float                 # N
    vs: float                 # N
    phi_vn: float             # N
    vs_excede_maximo: bool
    ratio: float              # Vu / φVn (≤1 cumple)
    cumple: bool


def verificar_viga_cortante(vu: float, bw: float, d: float, fc: float,
                            av: float, fyt: float, s: float, lam: float = 1.0) -> ResultadoCortante:
    """Verifica una viga a cortante: φVn = φ(Vc+Vs) vs Vu, con tope de Vs (§22.5.1.2)."""
    vc = cortante_concreto(bw, d, fc, lam)
    vs = cortante_acero(av, fyt, d, s)
    vs_max = cortante_acero_maximo(bw, d, fc)
    excede = vs > vs_max
    phi_vn = PHI_CORTANTE * (vc + vs)
    ratio = abs(vu) / phi_vn if phi_vn > 0 else float("inf")
    return ResultadoCortante(vc, vs, phi_vn, excede, ratio, ratio <= 1.0 + 1e-9 and not excede)

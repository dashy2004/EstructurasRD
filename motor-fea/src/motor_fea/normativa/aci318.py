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


# --------------------- Columnas (flexo-compresión P-M) ---------------------
EPS_CU = 0.003          # deformación última del hormigón (ACI 318-19 §22.2.2.1)
ES_DEFAULT = 200000.0   # módulo del acero (MPa)


def beta1(fc: float) -> float:
    """β1 del bloque de Whitney, ACI 318-19 §22.2.2.4.3: 0.85 (f'c≤28), −0.05/7 MPa, min 0.65."""
    if fc <= 28.0:
        return 0.85
    return max(0.65, 0.85 - 0.05 * (fc - 28.0) / 7.0)


def phi_por_deformacion(et: float, fy: float, es: float = ES_DEFAULT, estribos: bool = True) -> float:
    """φ por deformación neta de tracción εt (ACI 318-19 Tabla 21.2.2), miembros con estribos.

    φ = 0.65 (compresión, εt ≤ εty) → 0.90 (tracción, εt ≥ εty+0.003), lineal en medio.
    Para zunchos el piso es 0.75 en vez de 0.65.
    """
    phi_min = 0.65 if estribos else 0.75
    ey = fy / es
    if et <= ey:
        return phi_min
    if et >= ey + 0.003:
        return 0.90
    return phi_min + (0.90 - phi_min) * (et - ey) / 0.003


def axial_pura_nominal(ag: float, ast: float, fc: float, fy: float) -> float:
    """Po (N) — resistencia axial nominal a compresión pura: 0.85·f'c·(Ag−Ast) + fy·Ast."""
    return 0.85 * fc * (ag - ast) + fy * ast


def axial_maxima_diseno(ag: float, ast: float, fc: float, fy: float, estribos: bool = True) -> float:
    """φPn,max (N) — tope de compresión, ACI 318-19 §22.4.2.1: 0.80·φ·Po (estribos) / 0.85·φ·Po (zunchos)."""
    factor = 0.80 if estribos else 0.85
    phi = 0.65 if estribos else 0.75
    return factor * phi * axial_pura_nominal(ag, ast, fc, fy)


def profundidad_balanceada(d: float, fy: float, es: float = ES_DEFAULT) -> float:
    """Profundidad del eje neutro en el punto balanceado: c_b = εcu/(εcu+εy)·d."""
    ey = fy / es
    return EPS_CU / (EPS_CU + ey) * d


@dataclass(frozen=True)
class PuntoInteraccion:
    """Un punto (Pn, Mn) del diagrama de interacción, con su φ por εt. Compresión positiva."""
    c: float          # profundidad del eje neutro (mm)
    pn: float         # axial nominal (N)
    mn: float         # momento nominal respecto al centro geométrico (N·mm)
    et: float         # deformación neta de tracción de la capa extrema
    phi: float

    @property
    def phi_pn(self) -> float:
        return self.phi * self.pn

    @property
    def phi_mn(self) -> float:
        return self.phi * self.mn


def punto_interaccion(c: float, b: float, h: float, fc: float, fy: float,
                      capas: list[tuple[float, float]], es: float = ES_DEFAULT) -> PuntoInteraccion:
    """Punto del diagrama P-M para un eje neutro ``c`` (mm), por compatibilidad de deformaciones.

    ``capas`` = lista de ``(d_i, As_i)``: profundidad desde la fibra comprimida (mm)
    y área (mm²) de cada capa de refuerzo. Sección rectangular b×h.
    """
    if c <= 0:
        raise ValueError("c debe ser positivo.")
    a = min(beta1(fc) * c, h)
    cc = 0.85 * fc * b * a                       # resultante del bloque de hormigón (N)
    pn = cc
    mn = cc * (h / 2.0 - a / 2.0)

    d_max = max(d for d, _ in capas)
    for di, asi in capas:
        eps = EPS_CU * (c - di) / c              # + compresión, − tracción
        fs = max(-fy, min(fy, es * eps))
        fuerza = asi * fs
        if di <= a:                              # barra dentro del bloque → descontar hormigón desplazado
            fuerza -= asi * 0.85 * fc
        pn += fuerza
        mn += fuerza * (h / 2.0 - di)

    et = EPS_CU * (d_max - c) / c                 # tracción positiva
    phi = phi_por_deformacion(et, fy, es)
    return PuntoInteraccion(c, pn, mn, et, phi)


# ------------- Zapatas: punzonamiento (cortante en dos direcciones) -------------
ALPHA_S = {"interior": 40.0, "borde": 30.0, "esquina": 20.0}   # ACI 318-19 §22.6.5.3


def perimetro_critico(c1: float, c2: float, d: float, posicion: str = "interior") -> float:
    """Perímetro crítico bo (mm) a d/2 de la cara de la columna, ACI 318-19 §22.6.4.1.

    c1, c2 = lados de la columna (mm); ``posicion`` ∈ {interior, borde, esquina}.
    """
    if posicion == "interior":
        return 2.0 * (c1 + d) + 2.0 * (c2 + d)
    if posicion == "borde":
        return 2.0 * (c1 + d / 2.0) + (c2 + d)
    if posicion == "esquina":
        return (c1 + d / 2.0) + (c2 + d / 2.0)
    raise ValueError("posicion debe ser interior, borde o esquina.")


@dataclass(frozen=True)
class ResultadoPunzonamiento:
    bo: float                 # perímetro crítico (mm)
    beta_c: float             # relación lado largo/corto de la columna
    alpha_s: float            # 40 / 30 / 20
    vc_esfuerzo: float        # vc gobernante (MPa)
    vc_terminos: tuple[float, float, float]   # (base, βc, αs) en MPa
    vc: float                 # Vc nominal (N)
    phi_vc: float             # φVc (N)
    ratio: float              # Vu / φVc
    cumple: bool


def cortante_punzonamiento(vu: float, c1: float, c2: float, d: float, fc: float,
                           posicion: str = "interior", lam: float = 1.0) -> ResultadoPunzonamiento:
    """Verificación a punzonamiento de zapata/losa, ACI 318-19 §22.6.5.2.

    vc = min de los tres términos; Vc = vc·bo·d; φVc = 0.75·Vc; ratio = Vu/φVc.
    """
    bo = perimetro_critico(c1, c2, d, posicion)
    beta_c = max(c1, c2) / min(c1, c2)
    alpha_s = ALPHA_S[posicion]
    raiz = lam * math.sqrt(fc)

    v_base = 0.33 * raiz
    v_beta = 0.17 * (1.0 + 2.0 / beta_c) * raiz
    v_alpha = 0.083 * (2.0 + alpha_s * d / bo) * raiz
    vc_esf = min(v_base, v_beta, v_alpha)

    vc = vc_esf * bo * d
    phi_vc = PHI_CORTANTE * vc
    ratio = abs(vu) / phi_vc if phi_vc > 0 else float("inf")
    return ResultadoPunzonamiento(bo, beta_c, alpha_s, vc_esf,
                                  (v_base, v_beta, v_alpha), vc, phi_vc,
                                  ratio, ratio <= 1.0 + 1e-9)

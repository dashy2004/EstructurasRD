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


# --------------------- Losas: diseño de acero a flexión ---------------------
# Áreas nominales ASTM A615 en mm² (#3..#8 = 3/8"..1").
AREAS_BARRA_MM2 = {3: 71.0, 4: 129.0, 5: 199.0, 6: 284.0, 7: 387.0, 8: 510.0}
MODULO_ESP_MM = 10.0       # espaciamientos a múltiplos de 10 mm (1 cm)
ESP_MIN_MM = 75.0          # separación mínima práctica


def as_minimo_temperatura(b: float, h: float, fy: float) -> float:
    """As mínimo por retracción y temperatura (mm²), ACI 318-19 §24.4.3.2: ρ·b·h.

    ρ = 0.0020 (fy ≤ 350 MPa), 0.0018 (Grado 420) o max(0.0018·420/fy, 0.0014) (>420).
    Usa el espesor total ``h`` (no el canto útil).
    """
    if fy <= 350.0:
        rho = 0.0020
    elif fy <= 420.0:
        rho = 0.0018
    else:
        rho = max(0.0018 * 420.0 / fy, 0.0014)
    return rho * b * h


def espaciamiento_maximo_losa(h: float) -> float:
    """Espaciamiento máximo a flexión en losas en dos direcciones (mm), §8.7.2.2: min(2h, 450)."""
    return min(2.0 * h, 450.0)


@dataclass(frozen=True)
class ArmaduraLosaSel:
    numero_barra: int
    espaciamiento: float       # mm
    as_provista: float         # mm²/m
    cumple: bool
    disponer: str


def seleccionar_armadura_losa(as_diseno: float, esp_max: float) -> ArmaduraLosaSel:
    """Barra más pequeña cuyo espaciamiento práctico cubre ``as_diseno`` (mm²/m) y respeta min/max."""
    if as_diseno <= 0:
        ab = AREAS_BARRA_MM2[3]
        return ArmaduraLosaSel(3, esp_max, ab * 1000.0 / esp_max, True, f"#3 @ {esp_max:.0f} mm")
    ultima = None
    for n in (3, 4, 5, 6):
        ab = AREAS_BARRA_MM2[n]
        s = math.floor(ab * 1000.0 / as_diseno / MODULO_ESP_MM) * MODULO_ESP_MM
        if s > esp_max:
            s = esp_max
        as_prov = ab * 1000.0 / s
        cumple = ESP_MIN_MM <= s <= esp_max + 1e-9 and as_prov >= as_diseno - 1e-6
        ultima = ArmaduraLosaSel(n, s, as_prov, cumple, f"#{n} @ {s:.0f} mm")
        if s < ESP_MIN_MM:
            continue
        if as_prov >= as_diseno - 1e-6:
            return ultima
    return ultima  # type: ignore[return-value]


@dataclass(frozen=True)
class DisenoLosaFranja:
    as_requerido: float        # mm²/m
    as_minimo: float           # mm²/m (temperatura)
    as_diseno: float           # mm²/m = max(req, min)
    seccion_insuficiente: bool
    gobierna_minimo: bool
    armadura: ArmaduraLosaSel


def diseno_losa_franja(mu: float, b: float, h: float, d: float, fc: float, fy: float) -> DisenoLosaFranja:
    """Diseña una franja de losa de ancho ``b`` (típ. 1000 mm) a flexión (ACI 318-19, SI)."""
    as_req, insuf = as_requerido_flexion(mu, b, d, fc, fy)
    as_min = as_minimo_temperatura(b, h, fy)
    if insuf:
        return DisenoLosaFranja(as_req, as_min, float("nan"), True, False,
                                ArmaduraLosaSel(0, float("nan"), float("nan"), False, "SECCIÓN INSUFICIENTE"))
    as_dis = max(as_req, as_min)
    arm = seleccionar_armadura_losa(as_dis, espaciamiento_maximo_losa(h))
    return DisenoLosaFranja(as_req, as_min, as_dis, False, as_min >= as_req, arm)


# ===================== Diseño de pórtico (Fase 4b.1) =====================
def _diametro_barra(num: int) -> float:
    """Diámetro nominal de una barra #num (octavos de pulgada) en mm."""
    return num * 25.4 / 8.0


@dataclass(frozen=True)
class SeleccionBarras:
    numero_barra: int
    n_barras: int
    as_provista: float     # mm²
    cumple: bool


def seleccionar_barras(as_req: float, ancho_disponible: float, num: int = 5) -> SeleccionBarras:
    """Elige n barras #num (≥2) que cubran As y verifica que entren en el ancho disponible."""
    area = AREAS_BARRA_MM2[num]
    if math.isnan(as_req):                              # sección insuficiente a flexión
        return SeleccionBarras(num, 0, 0.0, False)
    n = max(2, math.ceil(as_req / area))
    as_provista = n * area
    d = _diametro_barra(num)
    entra = n * d + (n - 1) * 25.0 <= ancho_disponible  # separación libre mínima 25 mm
    return SeleccionBarras(num, n, as_provista, as_provista >= as_req and entra)


@dataclass(frozen=True)
class DisenoEstribo:
    numero_barra: int
    n_ramas: int
    espaciamiento: float   # mm
    av: float              # mm²
    vs_requerido: float    # N
    cumple: bool
    disponer: str


def disenar_estribo_viga(vu: float, bw: float, d: float, fc: float, fyt: float = 420.0,
                         num_estribo: int = 3, n_ramas: int = 2, lam: float = 1.0) -> DisenoEstribo:
    """Diseña los estribos de una viga para Vu (ACI 318-19 §22.5 / §9.6.3)."""
    if bw <= 0 or d <= 0 or fc <= 0 or fyt <= 0:
        raise ValueError("bw, d, fc y fyt deben ser positivos.")
    vu = abs(vu)
    vc = cortante_concreto(bw, d, fc, lam)
    av = n_ramas * AREAS_BARRA_MM2[num_estribo]
    s_max = min(d / 2.0, 600.0)
    vs_max = cortante_acero_maximo(bw, d, fc)
    if vu <= 0.5 * PHI_CORTANTE * vc:
        vs_req, s = 0.0, s_max                          # no requeridos por resistencia; s_max sin redondear
    else:
        vs_req = vu / PHI_CORTANTE - vc
        if vs_req <= 0:
            vs_req, s = 0.0, s_max                       # mínimos (el concreto basta)
        elif vs_req > vs_max:
            return DisenoEstribo(num_estribo, n_ramas, s_max, av, vs_req, False,
                                 "SECCIÓN INSUFICIENTE A CORTANTE")
        else:
            s_calc = min(av * fyt * d / vs_req, s_max)
            s = max(50.0, math.floor(s_calc / 25.0) * 25.0)  # múltiplo de 25 mm, ≥ 50
    vs_provisto = cortante_acero(av, fyt, d, s)
    cumple = vu <= PHI_CORTANTE * (vc + min(vs_provisto, vs_max)) + 1e-9
    return DisenoEstribo(num_estribo, n_ramas, s, av, vs_req, cumple,
                         f"E#{num_estribo} {n_ramas}R @ {s:.0f} mm")


@dataclass(frozen=True)
class DisenoColumna:
    pu: float              # N
    mu: float              # N·mm
    numero_barra: int
    n_barras: int
    rho: float
    cumple: bool
    disponer: str


def diagrama_interaccion(b: float, h: float, fc: float, fy: float,
                         capas: list[tuple[float, float]], n: int = 40) -> list[PuntoInteraccion]:
    """Envolvente P-M: barre el eje neutro c de 0.05·h a 2·h en n puntos."""
    return [punto_interaccion(0.05 * h + (2.0 - 0.05) * h * k / (n - 1), b, h, fc, fy, capas)
            for k in range(n)]


def momento_capacidad(phi_pn_demanda: float, diagrama: list[PuntoInteraccion]) -> float:
    """φMn (N·mm) de la envolvente φ interpolado al nivel axial φPn demandado; 0 si está fuera del rango.

    Interpola la envolvente CRUDA: NO aplica el tope φPn,max (§22.4.2.1, que necesita Ag/As), así que por
    encima del tope da un momento no conservador — úsese junto con ``axial_maxima_diseno`` (como hace
    ``disenar_columna_pm``). Asume φPn monótona en c (válido para el modelo simétrico de 2 capas).
    """
    pares = sorted((p.phi_pn, abs(p.phi_mn)) for p in diagrama)
    if phi_pn_demanda < pares[0][0] or phi_pn_demanda > pares[-1][0]:
        return 0.0
    for (p0, m0), (p1, m1) in zip(pares, pares[1:]):
        if p0 <= phi_pn_demanda <= p1:
            return m0 if p1 == p0 else m0 + (m1 - m0) * (phi_pn_demanda - p0) / (p1 - p0)
    return 0.0


def disenar_columna_pm(pu: float, mu: float, b: float, h: float, fc: float, fy: float,
                       recubrimiento: float, num: int = 8) -> DisenoColumna:
    """Diseña el refuerzo longitudinal de una columna para (Pu, Mu) por diagrama de interacción.

    Itera el nº de barras desde max(4 barras, ρ≈1% — ACI 10.6.1.1) hasta ρ_max=8%. Modela el acero como
    **2 capas extremas** (sup e inf, As/2 c/u): flexión uniaxial sobre el eje perpendicular a h; ignora
    barras intermedias/laterales y el caso biaxial. Verifica que (Pu, Mu) caiga dentro de la envolvente φ
    con el tope φPn,max.
    """
    if b <= 0 or h <= 0 or fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fy y recubrimiento deben ser positivos.")
    if h - 2 * recubrimiento <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    pu, mu = abs(pu), abs(mu)
    ag = b * h
    area = AREAS_BARRA_MM2[num]
    d_barra = _diametro_barra(num)
    n = max(4, math.ceil(0.01 * ag / area))
    ultimo_n = n
    while n * area / ag <= 0.08:
        as_total = n * area
        capas = [(recubrimiento + d_barra / 2.0, as_total / 2.0),
                 (h - recubrimiento - d_barra / 2.0, as_total / 2.0)]
        diagrama = diagrama_interaccion(b, h, fc, fy, capas)
        if pu <= axial_maxima_diseno(ag, as_total, fc, fy) and mu <= momento_capacidad(pu, diagrama):
            return DisenoColumna(pu, mu, num, n, as_total / ag, True, f"{n}#{num}")
        ultimo_n = n
        n += 1
    return DisenoColumna(pu, mu, num, ultimo_n, ultimo_n * area / ag, False, "SECCIÓN INSUFICIENTE")


# ===================== Estribo de columna (Fase 5C) =====================
def cortante_concreto_columna(bw: float, d: float, fc: float, nu: float, ag: float,
                              lam: float = 1.0) -> float:
    """Vc (N) de columna con axial — ACI 318-19 §22.5.6.1 (compresión) / §22.5.7.1 (tracción).

    nu: axial en N, COMPRESIÓN POSITIVA. Compresión aumenta Vc; tracción lo reduce (cap a 0).
    """
    if bw <= 0 or d <= 0 or fc <= 0 or ag <= 0:
        raise ValueError("bw, d, fc y ag deben ser positivos.")
    if nu >= 0:
        factor = 1.0 + nu / (14.0 * ag)
    else:
        factor = max(0.0, 1.0 + nu / (3.5 * ag))
    return 0.17 * factor * lam * math.sqrt(fc) * bw * d


def confinamiento_ash(s: float, bc: float, fc: float, fyt: float, ag: float, ach: float) -> float:
    """Ash requerido (mm²) de confinamiento — ACI 318-19 §18.7.5.4 (estribos rectangulares)."""
    if s <= 0 or bc <= 0 or fc <= 0 or fyt <= 0 or ag <= 0 or ach <= 0:
        raise ValueError("s, bc, fc, fyt, ag y ach deben ser positivos.")
    ratio = max(0.3 * (ag / ach - 1.0) * (fc / fyt), 0.09 * (fc / fyt))
    return s * bc * ratio


@dataclass(frozen=True)
class DisenoEstriboColumna:
    numero_barra: int
    n_ramas: int
    espaciamiento: float   # mm
    av: float              # mm²
    ash_provista: float    # mm²
    vs_requerido: float    # N
    cumple: bool
    disponer: str
    gobierna: str          # "cortante" | "confinamiento" | "detallado"


def disenar_estribo_columna(vu: float, pu: float, b: float, h: float, fc: float, db_long: float,
                            recubrimiento: float, fyt: float = 420.0, n_ramas: int = 2,
                            lam: float = 1.0) -> DisenoEstriboColumna:
    """Diseña el estribo de columna: cortante (Vc con axial) + confinamiento (ACI 18.7.5.4).

    vu, pu en N (pu COMPRESIÓN +); b, h, db_long, recubrimiento en mm. Escala la barra (#3→#4→#5)
    hasta que el Ash provisto confine a la separación; la separación final es la más exigente de
    cortante, confinamiento y detallado (§25.7.2.1). 'gobierna' indica cuál rigió.
    """
    if b <= 0 or h <= 0 or fc <= 0 or fyt <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fyt y recubrimiento deben ser positivos.")
    d = h - recubrimiento
    bc = b - 2.0 * recubrimiento
    if d <= 0 or bc <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    vu = abs(vu)
    ag = b * h
    ach = (b - 2.0 * recubrimiento) * (h - 2.0 * recubrimiento)
    vc = cortante_concreto_columna(b, d, fc, pu, ag, lam)
    vs_max = cortante_acero_maximo(b, d, fc)
    vs_req = vu / PHI_CORTANTE - vc
    if vs_req > vs_max:                                   # sección insuficiente a cortante
        av0 = n_ramas * AREAS_BARRA_MM2[3]
        return DisenoEstriboColumna(3, n_ramas, min(d / 2.0, 600.0), av0, av0, vs_req, False,
                                    "SECCIÓN INSUFICIENTE A CORTANTE", "cortante")
    ratio = max(0.3 * (ag / ach - 1.0) * (fc / fyt), 0.09 * (fc / fyt))
    s_so = min(0.25 * min(b, h), 6.0 * db_long, 150.0)   # §18.7.5.3 (so por hx = 150, simplificado)
    ultimo: DisenoEstriboColumna | None = None
    for num in (3, 4, 5):                                # escala la barra hasta confinar
        av = n_ramas * AREAS_BARRA_MM2[num]
        s_cortante = math.inf if vs_req <= 0 else av * fyt * d / vs_req
        s_conf = av / (ratio * bc) if ratio > 0 else math.inf
        s_det = min(16.0 * db_long, 48.0 * _diametro_barra(num), min(b, h))
        candidatos = {"cortante": s_cortante, "confinamiento": min(s_conf, s_so), "detallado": s_det}
        gobierna = min(candidatos, key=lambda k: candidatos[k])
        s = max(50.0, math.floor(min(candidatos.values()) / 25.0) * 25.0)
        cumple = av >= ratio * bc * s                    # Ash provista cubre la requerida a s
        ultimo = DisenoEstriboColumna(num, n_ramas, s, av, av, vs_req, cumple,
                                      f"E#{num} {n_ramas}R @ {s:.0f}", gobierna)
        if cumple:
            return ultimo
    return ultimo                                        # ni #5 confina → cumple=False

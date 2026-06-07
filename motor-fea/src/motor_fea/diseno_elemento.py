"""Diseño de refuerzo por elemento a partir de los esfuerzos del análisis (capa de composición).

Extrae la demanda (Pu, Mu, Vu) de un ``EsfuerzosElemento`` y la pasa a las rutinas de diseño de
``normativa.aci318``, convirtiendo de las unidades del modelo (N, m, N·m) a las de aci318 (N, mm, MPa).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from motor_fea.core.solver import EsfuerzosElemento
from motor_fea.normativa import aci318
from motor_fea.normativa.combinaciones import combinaciones_resistencia


@dataclass(frozen=True)
class DisenoViga:
    mu: float                                  # N·m (demanda)
    vu: float                                  # N
    flexion: aci318.SeleccionBarras | None
    estribo: aci318.DisenoEstribo
    cumple: bool
    disponer: str


def _demanda(esf: EsfuerzosElemento, n: int = 21) -> tuple[float, float]:
    """(Mu, Vu) del diagrama: Mu = max|My|,|Mz|; Vu = max|Vy|,|Vz| (N·m, N).

    Envolvente uniaxial-equivalente (se toma el mayor de las dos componentes, no biaxial); la torsión T
    se ignora — válido para los pórticos con cargas nodales que el motor resuelve hoy.
    """
    mu = vu = 0.0
    for _s, _n, vy, vz, _t, my, mz in esf.diagrama(n):
        mu = max(mu, abs(my), abs(mz))
        vu = max(vu, abs(vy), abs(vz))
    return mu, vu


def disenar_viga(esf: EsfuerzosElemento, b: float, h: float, fc: float = 21.0, fy: float = 420.0,
                 recubrimiento: float = 0.04) -> DisenoViga:
    """Diseña una viga (flexión + estribos) por la demanda de sus esfuerzos. b, h, rec en metros."""
    mu, vu = _demanda(esf)
    b_mm, d_mm = b * 1000.0, (h - recubrimiento) * 1000.0
    as_req, insuf = aci318.as_requerido_flexion(mu * 1000.0, b_mm, d_mm, fc, fy)
    as_dis = float("nan") if insuf else max(as_req, aci318.as_minimo_flexion(b_mm, d_mm, fc, fy))
    flexion = None if insuf else aci318.seleccionar_barras(as_dis, (b - 2 * recubrimiento) * 1000.0)
    estribo = aci318.disenar_estribo_viga(vu, b_mm, d_mm, fc, fy)
    cumple = (not insuf) and flexion is not None and flexion.cumple and estribo.cumple
    disponer = ("SECCIÓN INSUFICIENTE A FLEXIÓN" if insuf
                else f"{flexion.n_barras}#{flexion.numero_barra} + {estribo.disponer}")
    return DisenoViga(mu, vu, flexion, estribo, cumple, disponer)


def disenar_columna(esf: EsfuerzosElemento, b: float, h: float, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> aci318.DisenoColumna:
    """Diseña una columna (P-M) por la demanda de sus esfuerzos. b, h, rec en metros."""
    mu, _vu = _demanda(esf)          # cortante de columna (estribos) fuera de alcance en esta fase
    # se asume compresión: abs() toma la magnitud axial (una tracción se trataría como compresión)
    return aci318.disenar_columna_pm(abs(esf.axial), mu * 1000.0, b * 1000.0, h * 1000.0,
                                     fc, fy, recubrimiento * 1000.0)


# ===================== Diseño por combinaciones (Fase 5A.1) =====================
@dataclass(frozen=True)
class DisenoColumnaCombos:
    pu: float              # N (combo gobernante)
    mu: float              # N·mm (combo gobernante)
    numero_barra: int
    n_barras: int
    rho: float
    cumple: bool
    disponer: str
    combo_gobernante: str
    estribo: aci318.DisenoEstriboColumna
    combo_cortante: str
    muy: float          # N·m (combo gobernante)
    muz: float          # N·m
    utilizacion: float  # (Muy/φMny + Muz/φMnz) en la sección diseñada


@dataclass(frozen=True)
class DisenoVigaCombos:
    mu: float              # N·m (combo de flexión gobernante)
    vu: float              # N (combo de cortante gobernante)
    flexion: aci318.SeleccionBarras | None
    estribo: aci318.DisenoEstribo
    cumple: bool
    disponer: str
    combo_flexion: str
    combo_cortante: str


def _escalares_por_caso(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float]]:
    """{caso: (P, M, V)} — P axial con signo (N); M=max|My|,|Mz| (N·m); V=max|Vy|,|Vz| (N)."""
    out: dict[str, tuple[float, float, float]] = {}
    for caso, esf in esf_por_caso.items():
        mu = vu = 0.0
        for _s, _n, vy, vz, _t, my, mz in esf.diagrama(21):
            mu = max(mu, abs(my), abs(mz))
            vu = max(vu, abs(vy), abs(vz))
        out[caso] = (esf.axial, mu, vu)
    return out


def _demanda_por_combo(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float]]:
    """{combo: (Pu, Mu, Vu)} (N, N·m, N) — LRFD ACI §5.3.1, axial con signo, M/V por magnitud."""
    esc = _escalares_por_caso(esf_por_caso)
    combos_p = combinaciones_resistencia(**{caso: v[0] for caso, v in esc.items()})
    combos_m = combinaciones_resistencia(**{caso: v[1] for caso, v in esc.items()})
    combos_v = combinaciones_resistencia(**{caso: v[2] for caso, v in esc.items()})
    # Las 3 llamadas reciben las MISMAS etiquetas de caso → producen las MISMAS claves de combo
    # (incluidos los sufijos ± de W/E), así que combos_m[k]/combos_v[k] siempre existen.
    return {k: (combos_p[k], combos_m[k], combos_v[k]) for k in combos_p}


def _escalares_biaxial_por_caso(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float, float]]:
    """{caso: (P, My, Mz, V)} — P con signo (N); My=max|My|, Mz=max|Mz| (N·m); V=max|Vy|,|Vz| (N)."""
    out: dict[str, tuple[float, float, float, float]] = {}
    for caso, esf in esf_por_caso.items():
        my = mz = vu = 0.0
        for _s, _n, vy, vz, _t, m_y, m_z in esf.diagrama(21):
            my = max(my, abs(m_y))
            mz = max(mz, abs(m_z))
            vu = max(vu, abs(vy), abs(vz))
        out[caso] = (esf.axial, my, mz, vu)
    return out


def _demanda_biaxial_por_combo(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float, float]]:
    """{combo: (Pu, Muy, Muz, Vu)} (N, N·m, N·m, N) — LRFD; cada componente combinada por separado."""
    esc = _escalares_biaxial_por_caso(esf_por_caso)
    cp = combinaciones_resistencia(**{c: v[0] for c, v in esc.items()})
    cmy = combinaciones_resistencia(**{c: v[1] for c, v in esc.items()})
    cmz = combinaciones_resistencia(**{c: v[2] for c, v in esc.items()})
    cv = combinaciones_resistencia(**{c: v[3] for c, v in esc.items()})
    return {k: (cp[k], cmy[k], cmz[k], cv[k]) for k in cp}


def _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax) -> str:
    """Combo con mayor utilización biaxial (factor_biaxial); desempate por pu."""
    def clave(pu, muy, muz):
        f = aci318.factor_biaxial(pu, muy, muz, b_mm, h_mm, fc, fy, capas_y, capas_z)
        r_p = pu / pmax if pmax > 0 else math.inf
        return (max(f, r_p), pu)
    return max(dem, key=lambda k: clave(*dem[k]))


def disenar_columna_combos(esf_por_caso: dict[str, EsfuerzosElemento], b: float, h: float,
                           fc: float = 21.0, fy: float = 420.0, recubrimiento: float = 0.04,
                           num: int = 8) -> DisenoColumnaCombos:
    """Diseña una columna (P-M-M biaxial + estribo) cubriendo todos los combos LRFD. b,h,rec en m."""
    if b <= 0 or h <= 0 or fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fy y recubrimiento deben ser positivos.")
    b_mm, h_mm, rec_mm = b * 1000.0, h * 1000.0, recubrimiento * 1000.0
    if h_mm - 2 * rec_mm <= 0 or b_mm - 2 * rec_mm <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    biax = _demanda_biaxial_por_combo(esf_por_caso)
    combo_v = max(biax, key=lambda k: abs(biax[k][3]))
    estribo = aci318.disenar_estribo_columna(abs(biax[combo_v][3]), -biax[combo_v][0],
                                             b_mm, h_mm, fc, aci318._diametro_barra(num), rec_mm, fy)
    dem = {k: (abs(P), abs(My) * 1000.0, abs(Mz) * 1000.0) for k, (P, My, Mz, _V) in biax.items()}
    ag = b_mm * h_mm
    area = aci318.AREAS_BARRA_MM2[num]
    n = max(4, math.ceil(0.01 * ag / area))
    ultimo_n = n
    while n * area / ag <= 0.08:
        capas_y, capas_z = aci318._capas_biaxial(b_mm, h_mm, rec_mm, num, n)
        pmax = aci318.axial_maxima_diseno(ag, n * area, fc, fy)
        if all(pu <= pmax and aci318.factor_biaxial(pu, muy, muz, b_mm, h_mm, fc, fy, capas_y, capas_z) <= 1.0
               for pu, muy, muz in dem.values()):
            gob = _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax)
            util = aci318.factor_biaxial(dem[gob][0], dem[gob][1], dem[gob][2], b_mm, h_mm, fc, fy, capas_y, capas_z)
            return DisenoColumnaCombos(dem[gob][0], math.hypot(dem[gob][1], dem[gob][2]), num, n,
                                       n * area / ag, estribo.cumple, f"{n}#{num}", gob, estribo, combo_v,
                                       abs(biax[gob][1]), abs(biax[gob][2]), util)
        ultimo_n = n
        n += 1
    capas_y, capas_z = aci318._capas_biaxial(b_mm, h_mm, rec_mm, num, ultimo_n)
    pmax = aci318.axial_maxima_diseno(ag, ultimo_n * area, fc, fy)
    gob = _gobernante_columna_biaxial(dem, b_mm, h_mm, fc, fy, capas_y, capas_z, pmax)
    util = aci318.factor_biaxial(dem[gob][0], dem[gob][1], dem[gob][2], b_mm, h_mm, fc, fy, capas_y, capas_z)
    return DisenoColumnaCombos(dem[gob][0], math.hypot(dem[gob][1], dem[gob][2]), num, ultimo_n,
                               ultimo_n * area / ag, False, "SECCIÓN INSUFICIENTE", gob, estribo, combo_v,
                               abs(biax[gob][1]), abs(biax[gob][2]), util)


def disenar_viga_combos(esf_por_caso: dict[str, EsfuerzosElemento], b: float, h: float,
                        fc: float = 21.0, fy: float = 420.0, recubrimiento: float = 0.04) -> DisenoVigaCombos:
    """Diseña una viga (flexión + estribos) cubriendo todos los combos; reporta los gobernantes. b,h,rec en m."""
    demandas = _demanda_por_combo(esf_por_caso)                 # {combo: (Pu, Mu N·m, Vu N)}
    b_mm, d_mm = b * 1000.0, (h - recubrimiento) * 1000.0
    as_min = aci318.as_minimo_flexion(b_mm, d_mm, fc, fy)

    # Cortante: gobierna el combo de mayor |Vu| (más Vu → menor s) → diseñar para ese.
    combo_v = max(demandas, key=lambda k: abs(demandas[k][2]))
    vu_g = abs(demandas[combo_v][2])
    estribo = aci318.disenar_estribo_viga(vu_g, b_mm, d_mm, fc, fy)

    # Flexión: gobierna el combo de mayor |Mu| (mayor demanda → mayor As). As_req
    # es monótono en Mu; el piso As_min se aplica al dimensionar. Insuficiente → None.
    combo_flex = max(demandas, key=lambda k: abs(demandas[k][1]))
    as_req, insuf = aci318.as_requerido_flexion(abs(demandas[combo_flex][1]) * 1000.0,
                                                b_mm, d_mm, fc, fy)
    if insuf:
        return DisenoVigaCombos(abs(demandas[combo_flex][1]), vu_g, None, estribo, False,
                                "SECCIÓN INSUFICIENTE A FLEXIÓN", combo_flex, combo_v)
    flexion = aci318.seleccionar_barras(max(as_req, as_min), (b - 2 * recubrimiento) * 1000.0)
    cumple = flexion.cumple and estribo.cumple
    disponer = f"{flexion.n_barras}#{flexion.numero_barra} + {estribo.disponer}"
    return DisenoVigaCombos(abs(demandas[combo_flex][1]), vu_g, flexion, estribo, cumple,
                            disponer, combo_flex, combo_v)

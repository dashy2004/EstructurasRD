"""Diseño de refuerzo por elemento a partir de los esfuerzos del análisis (capa de composición).

Extrae la demanda (Pu, Mu, Vu) de un ``EsfuerzosElemento`` y la pasa a las rutinas de diseño de
``normativa.aci318``, convirtiendo de las unidades del modelo (N, m, N·m) a las de aci318 (N, mm, MPa).
"""
from __future__ import annotations

from dataclasses import dataclass

from motor_fea.core.solver import EsfuerzosElemento
from motor_fea.normativa import aci318


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

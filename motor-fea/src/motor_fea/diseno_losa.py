"""Diseño de losa por FEM — módulo de composición (caso de uso).

Cierra el ciclo **análisis → diseño** para losas, orquestando ``core.losa_fem``
(FEM de placa → momentos máximos) con ``normativa.aci318`` (acero a flexión).
Es lo que hace ``Losas.exe`` (momentos → armadura), pero por FEM propio y
auditable. ``core`` y ``normativa`` siguen sin conocerse.

Unidades de entrada: a,b,t en m; E en Pa; q en N/m²; f'c, fy en MPa;
recubrimiento en mm. El FEM trabaja en SI (N·m/m) y la conversión a N·mm/m para
ACI 318 (que opera en N·mm) se hace acá, en la frontera.
"""
from __future__ import annotations

from dataclasses import dataclass

from motor_fea.core.losa_fem import ResultadoLosa, resolver_losa_rectangular
from motor_fea.normativa import aci318
from motor_fea.normativa.aci318 import DisenoLosaFranja


@dataclass
class DisenoLosaFEM:
    """Resultado integrado: análisis FEM + diseño de acero (vano X/Y + apoyo)."""
    analisis: ResultadoLosa
    mu_x: float          # momento de vano en X (N·mm/m)
    mu_y: float          # momento de vano en Y (N·mm/m)
    mu_apoyo: float      # momento de apoyo (N·mm/m) — acero superior
    franja_x: DisenoLosaFranja        # acero inferior, franja X
    franja_y: DisenoLosaFranja        # acero inferior, franja Y
    franja_apoyo: DisenoLosaFranja    # acero superior (sobre apoyos)


def disenar_losa(a: float, b: float, nx: int, ny: int, E: float, nu: float, t: float,
                 q: float, fc_mpa: float, fy_mpa: float, recubrimiento_mm: float = 25.0,
                 borde: str = "simple") -> DisenoLosaFEM:
    """Analiza una losa por FEM y diseña su acero a flexión (ACI 318-19)."""
    res = resolver_losa_rectangular(a, b, nx, ny, E, nu, t, q, borde)
    h_mm = t * 1000.0
    d_mm = h_mm - recubrimiento_mm                       # canto útil aprox.
    mu_x = res.mx_max * 1000.0                           # N·m/m → N·mm/m
    mu_y = res.my_max * 1000.0
    mu_apoyo = res.m_apoyo_max * 1000.0
    fx = aci318.diseno_losa_franja(mu_x, 1000.0, h_mm, d_mm, fc_mpa, fy_mpa)
    fy_ = aci318.diseno_losa_franja(mu_y, 1000.0, h_mm, d_mm, fc_mpa, fy_mpa)
    fap = aci318.diseno_losa_franja(mu_apoyo, 1000.0, h_mm, d_mm, fc_mpa, fy_mpa)
    return DisenoLosaFEM(analisis=res, mu_x=mu_x, mu_y=mu_y, mu_apoyo=mu_apoyo,
                         franja_x=fx, franja_y=fy_, franja_apoyo=fap)

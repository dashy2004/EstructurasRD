"""Georreferencia de la maqueta: convierte coordenadas de escena (metros, locales)
⇄ lat/lng usando un ancla (plano tangente local alrededor del origen). Funciones
puras, sin I/O — se testean con asserts normales.

three.js usa el plano x–z como suelo (y = arriba). El ancla fija el origen
geográfico del solar, el rumbo (rotación del +Z de escena respecto al Norte) y la
escala (metros reales por unidad de escena; 1.0 = maqueta 1:1).
"""
from __future__ import annotations

import math
from dataclasses import dataclass

# Límites de República Dominicana (coinciden con los de Incidencias RD).
DR_LAT_MIN, DR_LAT_MAX = 17.36, 19.96
DR_LON_MIN, DR_LON_MAX = -72.0, -68.2

_M_POR_GRADO = 111_320.0   # metros por grado de latitud (aprox. esférica local)


@dataclass
class Ancla:
    lat0: float
    lon0: float
    rumbo_deg: float = 0.0
    escala: float = 1.0


def validar_rd(lat: float, lon: float) -> None:
    """Lanza ValueError si (lat, lon) cae fuera de los límites de RD."""
    if not (DR_LAT_MIN <= lat <= DR_LAT_MAX and DR_LON_MIN <= lon <= DR_LON_MAX):
        raise ValueError(f"Coordenada fuera de RD: lat={lat:.5f}, lon={lon:.5f}")


def escena_a_geo(x: float, z: float, ancla: Ancla) -> tuple[float, float]:
    """(x, z) de escena en metros → (lat, lon) en grados. La altura (y) no afecta."""
    th = math.radians(ancla.rumbo_deg)
    este = (x * math.cos(th) + z * math.sin(th)) * ancla.escala
    norte = (-x * math.sin(th) + z * math.cos(th)) * ancla.escala
    lat = ancla.lat0 + norte / _M_POR_GRADO
    lon = ancla.lon0 + este / (_M_POR_GRADO * math.cos(math.radians(ancla.lat0)))
    validar_rd(lat, lon)
    return lat, lon


def geo_a_escena(lat: float, lon: float, ancla: Ancla) -> tuple[float, float]:
    """(lat, lon) → (x, z) de escena en metros. Inversa de escena_a_geo (y se asume 0)."""
    norte = (lat - ancla.lat0) * _M_POR_GRADO
    este = (lon - ancla.lon0) * _M_POR_GRADO * math.cos(math.radians(ancla.lat0))
    th = math.radians(ancla.rumbo_deg)
    x = (este * math.cos(th) - norte * math.sin(th)) / ancla.escala
    z = (este * math.sin(th) + norte * math.cos(th)) / ancla.escala
    return x, z

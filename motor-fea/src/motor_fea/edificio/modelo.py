"""Dataclasses del modelo canónico del edificio + validación (Rebanada A).

Jerarquía: Proyecto → Edificio → Nivel → Losa, con elementos verticales
(Columna/Muro) CONTINUOS a nivel de edificio (atraviesan niveles). Unidades SI:
longitudes en metros, Z hacia arriba. Sin NumPy, sin I/O (eso vive en
``contrato.py``).
"""
from __future__ import annotations

from dataclasses import dataclass

# Catálogo de tipos de losa conocidos (validación). Ampliable sin romper el contrato.
TIPOS_LOSA = frozenset({"maciza", "aligerada", "reticular"})


@dataclass(frozen=True)
class CargasLosa:
    """Cargas de servicio de una losa, en kN/m² (muerta adicional y viva)."""
    muerta: float = 0.0
    viva: float = 0.0


@dataclass(frozen=True)
class Losa:
    """Losa de una planta. ``puntos`` es el contorno EN PLANTA ((x, y), ...);
    la elevación 3D la aporta el ``Nivel`` (no se almacena acá)."""
    id: int
    tipo: str
    espesor: float            # m
    puntos: tuple[tuple[float, float], ...]   # contorno en planta, ≥3 puntos
    cargas: CargasLosa = CargasLosa()


@dataclass(frozen=True)
class Nivel:
    """Planta del edificio (nivel = sistema unificado). ``cota`` es la única
    fuente de la elevación; las losas la heredan vía ``puntos_losa_3d``."""
    id: int
    nombre: str               # libre, independiente del nombre de las losas
    cota: float               # m, Z arriba
    losas: tuple[Losa, ...] = ()

    def puntos_losa_3d(self, losa: Losa) -> list[list[float]]:
        """Contorno 3D de una losa de este nivel: su (x, y) en planta a ``cota``."""
        return [[x, y, self.cota] for (x, y) in losa.puntos]

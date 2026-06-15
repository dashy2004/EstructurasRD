"""Dataclasses del modelo canónico del edificio + validación (Rebanada A).

Jerarquía: Proyecto → Edificio → Nivel → Losa, con elementos verticales
(Columna/Muro) CONTINUOS a nivel de edificio (atraviesan niveles). Unidades SI:
longitudes en metros, Z hacia arriba. Sin NumPy, sin I/O (eso vive en
``contrato.py``).
"""
from __future__ import annotations

from dataclasses import dataclass, field

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


@dataclass(frozen=True)
class Zapata:
    """Fundación aislada en la base de una vertical. Dimensiones en m."""
    ancho: float
    largo: float
    peralte: float


@dataclass(frozen=True)
class Columna:
    """Columna continua. ``posicion`` = (x, y) en planta; atraviesa
    ``cota_base → cota_tope`` (m)."""
    id: int
    posicion: tuple[float, float]
    base: float               # m (sección)
    peralte: float            # m (sección)
    cota_base: float
    cota_tope: float
    material: str
    zapata: Zapata | None = None


@dataclass(frozen=True)
class Muro:
    """Muro continuo. ``linea`` = ((x1, y1), (x2, y2)) en planta; atraviesa
    ``cota_base → cota_tope`` (m)."""
    id: int
    linea: tuple[tuple[float, float], tuple[float, float]]
    espesor: float            # m (sección)
    cota_base: float
    cota_tope: float
    material: str
    zapata: Zapata | None = None


@dataclass(frozen=True)
class Metadata:
    """Metadatos del proyecto (todos opcionales)."""
    nombre: str = ""
    autor: str = ""
    codigo_obra: str = ""
    ubicacion: str = ""
    fecha: str = ""


@dataclass(frozen=True)
class CargasGlobales:
    """Cargas globales del proyecto, en kN/m².

    ``muerta_adicional`` es la sobrecarga muerta (SDL): NO incluye el peso propio
    ni la muerta de la losa (``CargasLosa.muerta``). No sumar ambas como si fueran
    la misma cantidad."""
    muerta_adicional: float = 0.0
    viva: float = 0.0


@dataclass
class Edificio:
    """Edificio. Las verticales viven acá porque atraviesan niveles."""
    id: int
    nombre: str
    niveles: list[Nivel] = field(default_factory=list)
    elementos_verticales: list[Columna | Muro] = field(default_factory=list)

    def niveles_ordenados(self) -> list[Nivel]:
        """Niveles ordenados por cota creciente."""
        return sorted(self.niveles, key=lambda n: n.cota)

    def cota_minima(self) -> float:
        """Cota mínima del edificio (referencia de fundación). 0.0 si no hay niveles."""
        return min((n.cota for n in self.niveles), default=0.0)


@dataclass
class Proyecto:
    """Raíz del modelo canónico."""
    metadata: Metadata = field(default_factory=Metadata)
    cargas_globales: CargasGlobales = field(default_factory=CargasGlobales)
    combinaciones: list[str] = field(default_factory=list)
    edificios: list[Edificio] = field(default_factory=list)

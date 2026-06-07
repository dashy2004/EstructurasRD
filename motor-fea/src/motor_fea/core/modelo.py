"""Modelo de datos del análisis estructural (capa 1: análisis FEA puro).

Dataclasses puras de stdlib — sin NumPy — que describen un modelo de barras 3D.
El solver de rigidez directa (B1) consume estas estructuras y produce
desplazamientos, reacciones y esfuerzos internos. La capa normativa (B3) y la
capa de datos/geometría (B4) se mantienen separadas: este módulo no sabe nada
de ACI ni de IFC.

Convención de unidades del modelo: SI — longitudes en metros, fuerzas en
newtons, módulos en pascales. La conversión desde/hacia las unidades de obra
(ton·cm) vive en la capa de adaptación, no acá.

Grados de libertad: cada nodo tiene 6 (3 traslaciones ux,uy,uz + 3 rotaciones
rx,ry,rz), consistente con el elemento frame 3D de 12 GDL que implementa B1.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from enum import IntEnum


class GDL(IntEnum):
    """Índice local de los 6 grados de libertad por nodo."""
    UX = 0
    UY = 1
    UZ = 2
    RX = 3
    RY = 4
    RZ = 5


GDL_POR_NODO = 6

CASOS_CARGA = frozenset({"D", "L", "Lr", "S", "R", "W", "E"})   # tipos LRFD (ACI 318-19 §5.3.1)


@dataclass(frozen=True)
class Nodo:
    """Nodo de la malla. ``id`` es estable; las coordenadas están en metros."""
    id: int
    x: float
    y: float
    z: float = 0.0


@dataclass(frozen=True)
class Material:
    """Material elástico isótropo. ``E`` y ``G`` en pascales, densidad en kg/m³."""
    id: int
    E: float
    nu: float = 0.2
    densidad: float = 2400.0

    @property
    def G(self) -> float:
        """Módulo de corte derivado: G = E / (2·(1+ν))."""
        return self.E / (2.0 * (1.0 + self.nu))


@dataclass(frozen=True)
class Seccion:
    """Propiedades de sección de una barra (unidades SI, metros)."""
    id: int
    area: float          # A   (m²)
    inercia_y: float     # Iy  (m⁴) — flexión alrededor del eje local y
    inercia_z: float     # Iz  (m⁴) — flexión alrededor del eje local z
    constante_torsion: float  # J  (m⁴)


@dataclass(frozen=True)
class ElementoFrame:
    """Barra viga-columna de 2 nodos / 12 GDL.

    ``vector_referencia`` define el plano local (orientación de la sección);
    por defecto el eje global Z, salvo barras verticales donde B1 lo ajusta.
    """
    id: int
    nodo_i: int
    nodo_j: int
    material_id: int
    seccion_id: int
    vector_referencia: tuple[float, float, float] = (0.0, 0.0, 1.0)


@dataclass(frozen=True)
class Apoyo:
    """Restricción nodal: True = GDL restringido (fijo). Orden: ux,uy,uz,rx,ry,rz."""
    nodo_id: int
    ux: bool = False
    uy: bool = False
    uz: bool = False
    rx: bool = False
    ry: bool = False
    rz: bool = False

    def restricciones(self) -> tuple[bool, ...]:
        return (self.ux, self.uy, self.uz, self.rx, self.ry, self.rz)

    @staticmethod
    def empotrado(nodo_id: int) -> "Apoyo":
        return Apoyo(nodo_id, True, True, True, True, True, True)

    @staticmethod
    def articulado(nodo_id: int) -> "Apoyo":
        return Apoyo(nodo_id, True, True, True, False, False, False)


@dataclass(frozen=True)
class CargaNodal:
    """Carga puntual en un nodo. Fuerzas en N, momentos en N·m, ejes globales.

    ``caso`` es el tipo de carga LRFD (ACI 318-19 §5.3.1): D, L, Lr, S, R, W, E.
    """
    nodo_id: int
    fx: float = 0.0
    fy: float = 0.0
    fz: float = 0.0
    mx: float = 0.0
    my: float = 0.0
    mz: float = 0.0
    caso: str = "D"

    def componentes(self) -> tuple[float, ...]:
        return (self.fx, self.fy, self.fz, self.mx, self.my, self.mz)


@dataclass
class ModeloEstructural:
    """Contenedor del modelo analítico. Validación de integridad referencial."""
    nodos: list[Nodo] = field(default_factory=list)
    materiales: list[Material] = field(default_factory=list)
    secciones: list[Seccion] = field(default_factory=list)
    elementos: list[ElementoFrame] = field(default_factory=list)
    apoyos: list[Apoyo] = field(default_factory=list)
    cargas: list[CargaNodal] = field(default_factory=list)

    @property
    def n_gdl(self) -> int:
        """Total de grados de libertad del sistema (6 por nodo)."""
        return len(self.nodos) * GDL_POR_NODO

    def indice_nodos(self) -> dict[int, int]:
        """Mapa id-de-nodo → posición (0..n-1) para ensamblar la matriz global."""
        return {n.id: i for i, n in enumerate(self.nodos)}

    def validar(self) -> list[str]:
        """Devuelve una lista de errores de integridad (vacía si el modelo es válido)."""
        errores: list[str] = []
        ids_nodo = {n.id for n in self.nodos}
        ids_mat = {m.id for m in self.materiales}
        ids_sec = {s.id for s in self.secciones}

        if len(ids_nodo) != len(self.nodos):
            errores.append("IDs de nodo duplicados.")

        for e in self.elementos:
            for ref, conj, etiq in (
                (e.nodo_i, ids_nodo, "nodo_i"),
                (e.nodo_j, ids_nodo, "nodo_j"),
                (e.material_id, ids_mat, "material_id"),
                (e.seccion_id, ids_sec, "seccion_id"),
            ):
                if ref not in conj:
                    errores.append(f"Elemento {e.id}: {etiq}={ref} no existe.")
            if e.nodo_i == e.nodo_j:
                errores.append(f"Elemento {e.id}: nodo_i y nodo_j son el mismo.")

        for a in self.apoyos:
            if a.nodo_id not in ids_nodo:
                errores.append(f"Apoyo en nodo {a.nodo_id} inexistente.")
        for c in self.cargas:
            if c.nodo_id not in ids_nodo:
                errores.append(f"Carga en nodo {c.nodo_id} inexistente.")
            if c.caso not in CASOS_CARGA:
                errores.append(f"Carga en nodo {c.nodo_id}: caso '{c.caso}' inválido.")
        return errores

    def es_valido(self) -> bool:
        return not self.validar()

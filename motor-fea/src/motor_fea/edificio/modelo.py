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

    def niveles_atravesados(self, vertical) -> list[Nivel]:
        """Niveles cuya cota cae en ``[cota_base, cota_tope]`` de la vertical.

        Base explícita para la futura bajada de cargas: una columna/muro continuo
        queda conectado a todos los niveles que atraviesa."""
        return [n for n in self.niveles_ordenados()
                if vertical.cota_base <= n.cota <= vertical.cota_tope]


@dataclass
class Proyecto:
    """Raíz del modelo canónico."""
    metadata: Metadata = field(default_factory=Metadata)
    cargas_globales: CargasGlobales = field(default_factory=CargasGlobales)
    combinaciones: list[str] = field(default_factory=list)
    edificios: list[Edificio] = field(default_factory=list)

    def validar(self) -> list[str]:
        """Lista de errores legibles (vacía si el modelo es válido)."""
        errores: list[str] = []
        if len({e.id for e in self.edificios}) != len(self.edificios):
            errores.append("IDs de edificio duplicados.")
        for edi in self.edificios:
            errores.extend(_validar_niveles(edi))
            errores.extend(_validar_verticales(edi))
            errores.extend(_validar_losas(edi))
        return errores

    def es_valido(self) -> bool:
        return not self.validar()


def _validar_niveles(edi: Edificio) -> list[str]:
    errores: list[str] = []
    if not edi.niveles:
        errores.append(f"Edificio {edi.id}: debe tener al menos un nivel.")
        return errores
    if len({n.id for n in edi.niveles}) != len(edi.niveles):
        errores.append(f"Edificio {edi.id}: IDs de nivel duplicados.")
    cotas = [n.cota for n in edi.niveles]
    if len(set(cotas)) != len(cotas):
        errores.append(f"Edificio {edi.id}: cotas de nivel duplicadas "
                       "(deben ser únicas; el orden lo da niveles_ordenados).")
    return errores


def _validar_verticales(edi: Edificio) -> list[str]:
    errores: list[str] = []
    # NOTA: las cotas son literales del contrato (no aritmética); la comparación
    # exacta de floats con ``in cotas_nivel`` es segura mientras eso se mantenga.
    cotas_nivel = {n.cota for n in edi.niveles}
    cota_min = edi.cota_minima()  # siempre ∈ cotas_nivel cuando hay niveles (es min de ellas)
    if len({v.id for v in edi.elementos_verticales}) != len(edi.elementos_verticales):
        errores.append(f"Edificio {edi.id}: IDs de vertical duplicados.")
    for v in edi.elementos_verticales:
        et = f"Edificio {edi.id} vertical {v.id}"
        if v.cota_base >= v.cota_tope:
            errores.append(f"{et}: cota_base ({v.cota_base}) debe ser menor que cota_tope ({v.cota_tope}).")
        if v.cota_tope not in cotas_nivel:
            errores.append(f"{et}: cota_tope ({v.cota_tope}) no alineada con ningún nivel.")
        # La base puede ser fundación (≤ cota mínima); si no, debe alinear con un nivel.
        if v.cota_base not in cotas_nivel and v.cota_base > cota_min:
            errores.append(f"{et}: cota_base ({v.cota_base}) no alineada con ningún nivel ni con la fundación.")
        for valor, etiq in _dimensiones_vertical(v):
            if valor <= 0:
                errores.append(f"{et}: {etiq} debe ser positivo.")
        if v.zapata is not None:
            for etiq, valor in (("ancho", v.zapata.ancho), ("largo", v.zapata.largo),
                                ("peralte", v.zapata.peralte)):
                if valor <= 0:
                    errores.append(f"{et}: zapata.{etiq} debe ser positivo.")
    return errores


def _dimensiones_vertical(v: Columna | Muro) -> list[tuple[float, str]]:
    """[(valor, etiqueta), ...] de las dimensiones de sección de una vertical."""
    if isinstance(v, Columna):
        return [(v.base, "base"), (v.peralte, "peralte")]
    if isinstance(v, Muro):
        return [(v.espesor, "espesor")]
    # Simétrico con el contrato: un tipo desconocido falla en vez de saltarse la validación.
    raise TypeError(f"Tipo de elemento vertical no soportado: {type(v).__name__!r}.")


def _validar_losas(edi: Edificio) -> list[str]:
    errores: list[str] = []
    for nivel in edi.niveles:
        if len({losa.id for losa in nivel.losas}) != len(nivel.losas):
            errores.append(f"Edificio {edi.id} nivel {nivel.id}: IDs de losa duplicados.")
        for losa in nivel.losas:
            et = f"Edificio {edi.id} nivel {nivel.id} losa {losa.id}"
            if len(losa.puntos) < 3:
                errores.append(f"{et}: el contorno necesita al menos 3 puntos.")
            if losa.espesor <= 0:
                errores.append(f"{et}: espesor debe ser positivo.")
            if losa.tipo not in TIPOS_LOSA:
                errores.append(f"{et}: tipo '{losa.tipo}' fuera del catálogo {sorted(TIPOS_LOSA)}.")
    return errores

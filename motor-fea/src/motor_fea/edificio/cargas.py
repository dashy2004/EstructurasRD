"""Bajada de cargas losa→bordes (Rebanada C).

Traduce la carga superficial de una losa (``q`` en kN/m²) a cargas lineales
sobre sus bordes de apoyo (kN/m), aptas como entrada distribuida para el motor
de barras. Port del tipo de dominio .NET ``RepartoCargaLosa.cs`` (método clásico
de **áreas tributarias** a 45°), extendido a:

  * carga **muerta y viva separadas** (habilita combinaciones LRFD aguas abajo), y
  * un **fallback poligonal** por perímetro para paños no rectangulares.

Funciones puras, sin I/O. Unidades: ``q`` en kN/m², longitudes en m → fuerzas en
kN, líneas en kN/m. No aplica las cargas a la malla FEA (eso es la rebanada C2).
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from enum import Enum

from motor_fea.edificio.modelo import Losa

_TOL = 1e-9


class FormaCarga(Enum):
    """Forma de la carga que un borde recibe del paño."""
    TRIANGULAR = "triangular"     # borde corto de un rectángulo
    TRAPEZOIDAL = "trapezoidal"   # borde largo de un rectángulo
    UNIFORME = "uniforme"         # fallback poligonal (por perímetro)


@dataclass(frozen=True)
class CargaBorde:
    """Carga que un borde de losa transmite a su apoyo."""
    indice_borde: int             # lado del contorno (0..n-1), en orden de `puntos`
    longitud: float               # m
    forma: FormaCarga
    fuerza_total: float           # kN   = q · área tributaria del borde
    linea_uniforme_equivalente: float  # kN/m = fuerza_total / longitud
    intensidad_pico: float        # kN/m = intensidad máxima del triángulo/trapecio


@dataclass(frozen=True)
class RepartoDireccion:
    """Reparto de un caso de carga (muerta O viva) a los bordes de un paño."""
    bordes: tuple[CargaBorde, ...]   # uno por lado del contorno
    carga_total: float               # kN = q · área del paño


@dataclass(frozen=True)
class RepartoLosa:
    """Reparto completo de una losa: cargas de borde de muerta y de viva."""
    losa_id: int
    rectangular: bool
    muerta: RepartoDireccion
    viva: RepartoDireccion


def repartir_losa(losa: Losa) -> RepartoLosa:
    """Reparte las cargas (muerta y viva) de ``losa`` a los bordes de su contorno.

    Paño rectangular → áreas tributarias 45° (bordes cortos triangulares, largos
    trapezoidales). Paño cualquier otro → reparto por perímetro (uniforme,
    conserva la fuerza total). Carga no positiva → reparto nulo para ese caso.
    """
    puntos = losa.puntos
    if len(puntos) < 3:
        raise ValueError(
            f"losa {losa.id}: el contorno necesita al menos 3 puntos para repartir cargas."
        )

    dims = _dims_rectangulo(puntos)
    rectangular = dims is not None

    def caso(q: float) -> RepartoDireccion:
        if q <= 0:
            return RepartoDireccion((), 0.0)
        if rectangular:
            return _reparto_rectangular(puntos, dims, q)
        return _reparto_poligonal(puntos, q)

    return RepartoLosa(
        losa_id=losa.id,
        rectangular=rectangular,
        muerta=caso(losa.cargas.muerta),
        viva=caso(losa.cargas.viva),
    )


def _dims_rectangulo(puntos) -> tuple[float, float] | None:
    """``(lx, ly)`` si ``puntos`` es un rectángulo alineado a ejes; si no, ``None``."""
    if len(puntos) != 4:
        return None
    xs = {round(x, 9) for x, _ in puntos}
    ys = {round(y, 9) for _, y in puntos}
    if len(xs) != 2 or len(ys) != 2:
        return None
    esquinas = {(x, y) for x in xs for y in ys}
    if {(round(x, 9), round(y, 9)) for x, y in puntos} != esquinas:
        return None
    (x0, x1), (y0, y1) = sorted(xs), sorted(ys)
    return (x1 - x0, y1 - y0)


def _lados(puntos) -> list[float]:
    """Longitud de cada lado del contorno, en orden (cierra el polígono)."""
    n = len(puntos)
    return [math.hypot(puntos[(i + 1) % n][0] - puntos[i][0],
                       puntos[(i + 1) % n][1] - puntos[i][1]) for i in range(n)]


def _reparto_rectangular(puntos, dims, q: float) -> RepartoDireccion:
    lx, ly = dims
    a, b = min(lx, ly), max(lx, ly)        # corto, largo
    pico = q * a / 2.0                       # intensidad pico común
    f_corto = q * a * a / 4.0                # triángulo
    f_largo = q * (a / 4.0) * (2.0 * b - a)  # trapecio

    bordes = []
    for i, L in enumerate(_lados(puntos)):
        if math.isclose(L, a, abs_tol=_TOL):  # borde corto → triangular
            bordes.append(CargaBorde(i, L, FormaCarga.TRIANGULAR,
                                     f_corto, f_corto / a, pico))
        else:                                 # borde largo → trapezoidal
            bordes.append(CargaBorde(i, L, FormaCarga.TRAPEZOIDAL,
                                     f_largo, f_largo / b, pico))
    return RepartoDireccion(tuple(bordes), q * lx * ly)


def _reparto_poligonal(puntos, q: float) -> RepartoDireccion:
    """Fallback por perímetro: fuerza total proporcional a la longitud de cada lado.

    NO es tributaria real (no traza líneas a 45°): reparte ``Q = q·A`` de forma
    isótropa, dando la misma línea uniforme ``Q/perímetro`` a todos los bordes.
    Conserva la fuerza total; es conservador y se sustituirá por *straight
    skeleton* si el dominio lo requiere.
    """
    area = abs(_area_shoelace(puntos))
    lados = _lados(puntos)
    perimetro = sum(lados)
    if area <= _TOL or perimetro <= _TOL:
        return RepartoDireccion((), 0.0)
    Q = q * area
    w = Q / perimetro
    bordes = tuple(
        CargaBorde(i, L, FormaCarga.UNIFORME, w * L, w, w)
        for i, L in enumerate(lados)
    )
    return RepartoDireccion(bordes, Q)


def _area_shoelace(puntos) -> float:
    n = len(puntos)
    return sum(puntos[i][0] * puntos[(i + 1) % n][1]
               - puntos[(i + 1) % n][0] * puntos[i][1]
               for i in range(n)) / 2.0

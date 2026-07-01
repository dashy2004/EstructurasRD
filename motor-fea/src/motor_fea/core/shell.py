"""Elemento shell plano local (24×24) — capa 1, análisis FEM.

Cuadrilátero plano de 4 nodos, 6 GDL/nodo en el orden del frame del modelo
`[ux, uy, uz, θx, θy, θz]`. En un shell **plano** la membrana (en el plano) y la
flexión de placa (fuera del plano) **no se acoplan**: la rigidez 24×24 se ensambla
de dos bloques 12×12 disjuntos.

- `ux, uy` → membrana Q4 de tensión plana (M1, `core/membrana.py`, isoparamétrica
  general).
- `uz, θx, θy` → placa de flexión ACM (`core/placa.py`). LIMITACIÓN: la placa ACM
  es **rectangular**; M2a la alimenta con la geometría rectangular equivalente del
  elemento. Los muros llenos (M3) se mallan en rectángulos, donde esto aplica. La
  membrana, en cambio, sí es isoparamétrica general.
- `θz` → drilling (rotación en torno a la normal local). Estabilización de
  **Hughes–Brezzi** (penalización rotacional, NO física): `E_drill = ½γ∫(θz−ω)²dA`
  con `ω = ½(∂uy/∂x − ∂ux/∂y)`. Se anula bajo rotación rígida en el plano (`θz=ω`),
  preservando los 6 modos de cuerpo rígido, a cambio de acoplar `(ux,uy)` con `θz`.

Unidades SI: longitudes en m, E en Pa, t en m.
"""
from __future__ import annotations

import math

from motor_fea.core import membrana, placa


def _rigidez_drilling(nodos_xy: list[tuple[float, float]],
                      E: float, t: float, gamma: float) -> list[list[float]]:
    """Bloque 12×12 de drilling Hughes–Brezzi en GDL (ux,uy,θz)×4.

    Penalización ``K = γ ∫∫ gᵀg dA`` por Gauss 2×2, con
    ``g·d = θz − ω`` y ``ω = ½(∂uy/∂x − ∂ux/∂y)``. Por nodo ``a``:
    ``g[3a]=½∂N_a/∂y`` (ux), ``g[3a+1]=−½∂N_a/∂x`` (uy), ``g[3a+2]=N_a`` (θz).
    """
    K = [[0.0] * 12 for _ in range(12)]
    for xi, wxi in membrana._GAUSS2:
        for eta, weta in membrana._GAUSS2:
            B, detJ = membrana._matriz_B(nodos_xy, xi, eta)
            g = [0.0] * 12
            for a in range(4):
                xa, ea = membrana._ESQUINAS[a]
                Na = 0.25 * (1.0 + xa * xi) * (1.0 + ea * eta)
                dndx = B[0][2 * a]          # ∂N_a/∂x (de la fila εxx de B)
                dndy = B[1][2 * a + 1]      # ∂N_a/∂y (de la fila εyy de B)
                g[3 * a + 0] = 0.5 * dndy
                g[3 * a + 1] = -0.5 * dndx
                g[3 * a + 2] = Na
            peso = wxi * weta * detJ * gamma
            for i in range(12):
                gi = peso * g[i]
                fila = K[i]
                for j in range(12):
                    fila[j] += gi * g[j]
    return K


def _dims_rectangulo(nodos_xy: list[tuple[float, float]]) -> tuple[float, float]:
    """Lados (lx, ly) de un rectángulo eje-alineado: |n1−n0| y |n2−n1|.

    Precondición: ``nodos_xy`` debe ser un rectángulo eje-alineado con nodos en
    orden antihorario ``[(x0,y0),(x0+lx,y0),(x0+lx,y0+ly),(x0,y0+ly)]``.
    Lanza ``ValueError`` si no se cumple esta condición, si ``lx≤0`` o ``ly≤0``
    (elemento degenerado/colineal). M2b debe suministrar coordenadas locales
    rectangulares eje-alineadas; el bloque de placa ACM no soporta quads generales.

    Alimenta a la placa ACM (rectangular). Para muros mallados en rectángulos (M3)
    coincide con la geometría real del elemento.
    """
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = nodos_xy
    lx = math.hypot(x1 - x0, y1 - y0)
    ly = math.hypot(x2 - x1, y2 - y1)

    if lx <= 0.0 or ly <= 0.0:
        raise ValueError(
            f"Shell M2a: elemento degenerado — lx={lx}, ly={ly} deben ser > 0. "
            "Verifique que los nodos no sean colineales."
        )

    tol = 1e-9 * max(lx, ly)

    # Nodo 0 en (x0,y0), nodo 1 en (x0+lx, y0), nodo 2 en (x0+lx, y0+ly),
    # nodo 3 en (x0, y0+ly): rectángulo eje-alineado exacto.
    if (abs(y1 - y0) > tol          # n1 debe tener misma y que n0
            or abs(x2 - x1) > tol   # n2 debe tener misma x que n1
            or abs(x3 - x0) > tol   # n3 debe tener misma x que n0
            or abs(y3 - y2) > tol   # n3 debe tener misma y que n2
            or abs((x1 - x0) - lx) > tol
            or abs((y2 - y1) - ly) > tol):
        raise ValueError(
            "Shell M2a: el bloque de placa ACM requiere un rectángulo eje-alineado. "
            f"Nodos recibidos: {nodos_xy}. "
            "M2b debe suministrar coordenadas locales rectangulares eje-alineadas; "
            "quads generales (trapecios, rombos, etc.) no son soportados en M2a."
        )

    return lx, ly


def rigidez_shell(nodos_xy: list[tuple[float, float]],
                  E: float, nu: float, t: float,
                  gamma: float | None = None) -> list[list[float]]:
    """Rigidez local 24×24 del shell plano cuadrilátero de 4 nodos.

    ``nodos_xy`` = 4 pares (x, y) en el plano local, orden antihorario. 6 GDL/nodo
    en orden ``[ux, uy, uz, θx, θy, θz]``; índice global ``6·a + d``. ``gamma`` =
    factor de drilling; si ``None`` usa ``E·t``. Devuelve K simétrica (24×24).
    """
    if gamma is None:
        gamma = E * t

    Km = membrana.rigidez_membrana(nodos_xy, E, nu, t)         # 8×8  [ux,uy]×4
    lx, ly = _dims_rectangulo(nodos_xy)
    Kp = placa.rigidez_placa(lx, ly, E, nu, t)                 # 12×12 [w,θx,θy]×4
    Kd = _rigidez_drilling(nodos_xy, E, t, gamma)              # 12×12 [ux,uy,θz]×4

    K = [[0.0] * 24 for _ in range(24)]

    # Bloque membrana+drilling → GDL shell (ux,uy,θz) = 6a+{0,1,5}.
    idx_md = [6 * a + d for a in range(4) for d in (0, 1, 5)]
    # Embeber membrana (orden [ux,uy]×4, índice 2a+c) en el bloque md (3a+c).
    for a in range(4):
        for c in range(2):
            for b in range(4):
                for e in range(2):
                    K[idx_md[3 * a + c]][idx_md[3 * b + e]] += Km[2 * a + c][2 * b + e]
    # Sumar el drilling (ya en GDL (ux,uy,θz)×4 = orden del bloque md).
    for i in range(12):
        for j in range(12):
            K[idx_md[i]][idx_md[j]] += Kd[i][j]

    # Bloque placa → GDL shell (uz,θx,θy) = 6a+{2,3,4}.
    idx_p = [6 * a + d for a in range(4) for d in (2, 3, 4)]
    for i in range(12):
        for j in range(12):
            K[idx_p[i]][idx_p[j]] += Kp[i][j]

    return K


# --------------------------------------------------------------------------- #
# Capa global (M2b): el shell plano ubicado en 3D. La rigidez local 24×24 se
# expresa en el marco global mediante K_global = Tᵀ·K_local·T con T ortogonal
# (rotación pura → preserva la energía de deformación).
# --------------------------------------------------------------------------- #
Vec3 = tuple[float, float, float]


def _resta(a: Vec3, b: Vec3) -> Vec3:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cruz(a: Vec3, b: Vec3) -> Vec3:
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def _dot3(a: Vec3, b: Vec3) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _norm3(v: Vec3) -> Vec3:
    n = math.sqrt(_dot3(v, v))
    if n < 1e-300:
        raise ValueError("Vector nulo al normalizar la tríada del shell.")
    return (v[0] / n, v[1] / n, v[2] / n)


def _marco_shell(nodos3d: list[Vec3]) -> tuple[Vec3, Vec3, Vec3]:
    """Tríada local ortonormal (ex, ey, ez) del cuadrilátero 3D.

    ``ex`` = primer lado ``p1−p0`` normalizado; ``ez`` = normal al plano
    (``(p1−p0)×(p2−p0)`` normalizada); ``ey = ez×ex`` cierra la base dextrógira.
    ``ey`` sale unitario por ser producto de dos unitarios ortogonales.
    """
    p0, p1, p2, _ = nodos3d
    v01 = _resta(p1, p0)
    v02 = _resta(p2, p0)
    ex = _norm3(v01)
    ez = _norm3(_cruz(v01, v02))
    ey = _cruz(ez, ex)
    return ex, ey, ez


def _proyectar_2d(nodos3d: list[Vec3], ex: Vec3, ey: Vec3) -> list[tuple[float, float]]:
    """Proyecta los 4 nodos 3D al plano local (origen en ``p0``): ``(v·ex, v·ey)``.

    Una rotación rígida preserva distancias, y ``ex`` se ancla al lado ``p0→p1``:
    la proyección deshace la orientación 3D y recupera el rectángulo local, que es
    lo que ``rigidez_shell`` (placa ACM rectangular) espera.
    """
    p0 = nodos3d[0]
    out = []
    for p in nodos3d:
        v = _resta(p, p0)
        out.append((_dot3(v, ex), _dot3(v, ey)))
    return out


def _transformacion_24(ex: Vec3, ey: Vec3, ez: Vec3) -> list[list[float]]:
    """T (24×24) global→local: 8 bloques diagonales de R=[ex,ey,ez] (filas=ejes
    locales), uno por cada terna de traslación y de rotación de los 4 nodos."""
    R = [list(ex), list(ey), list(ez)]
    T = [[0.0] * 24 for _ in range(24)]
    for blk in range(8):
        o = blk * 3
        for i in range(3):
            for j in range(3):
                T[o + i][o + j] = R[i][j]
    return T


def _congruencia(T: list[list[float]], K: list[list[float]]) -> list[list[float]]:
    """Producto de congruencia Tᵀ·K·T (24×24)."""
    TtK = [[sum(T[k][i] * K[k][j] for k in range(24)) for j in range(24)]
           for i in range(24)]
    return [[sum(TtK[i][k] * T[k][j] for k in range(24)) for j in range(24)]
            for i in range(24)]


def rigidez_shell_global(nodos3d: list[Vec3],
                         E: float, nu: float, t: float,
                         gamma: float | None = None) -> list[list[float]]:
    """Rigidez 24×24 del shell en coordenadas globales.

    ``nodos3d`` = 4 nodos ``(x, y, z)`` en orden antihorario. Deriva la tríada
    local, proyecta al plano para alimentar ``rigidez_shell`` (M2a) y rota el
    resultado al marco global con ``Tᵀ·K·T``. GDL/nodo ``[ux,uy,uz,θx,θy,θz]``,
    índice global ``6·a + d``.
    """
    ex, ey, ez = _marco_shell(nodos3d)
    nodos_xy = _proyectar_2d(nodos3d, ex, ey)
    Kl = rigidez_shell(nodos_xy, E, nu, t, gamma)
    T = _transformacion_24(ex, ey, ez)
    return _congruencia(T, Kl)

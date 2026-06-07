"""Solver de rigidez directa para marcos 3D (elemento frame de 12 GDL).

Capa 1 (análisis FEA puro). Implementación en **Python puro** (stdlib) para
correr sin NumPy en cualquier intérprete; el elemento Hermitiano reproduce
exactamente la solución de Euler-Bernoulli para cargas nodales, así que los
casos de validación cerrada (voladizo, axial, torsión) dan error ~precisión de
máquina. NumPy/SciPy entran como aceleración a escala (B5), no para corrección.

Unidades: SI (metros, newtons, pascales) — las del :mod:`motor_fea.core.modelo`.

Referencias de la matriz de rigidez local 12×12: McGuire & Gallagher,
*Matrix Structural Analysis*; H.P. Gavin, Duke CEE 421L.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from motor_fea.core.modelo import GDL_POR_NODO, ModeloEstructural


# --------------------------------------------------------------------------
# Álgebra lineal mínima (densa) — suficiente para los tamaños de validación.
# --------------------------------------------------------------------------
Matriz = list[list[float]]
Vector = list[float]


def _ceros(n: int, m: int) -> Matriz:
    return [[0.0] * m for _ in range(n)]


def _matmul(a: Matriz, b: Matriz) -> Matriz:
    n, k, m = len(a), len(b), len(b[0])
    out = _ceros(n, m)
    for i in range(n):
        ai = a[i]
        for p in range(k):
            aip = ai[p]
            if aip == 0.0:
                continue
            bp = b[p]
            oi = out[i]
            for j in range(m):
                oi[j] += aip * bp[j]
    return out


def _transpuesta(a: Matriz) -> Matriz:
    return [list(col) for col in zip(*a)]


def _matvec(a: Matriz, x: Vector) -> Vector:
    return [sum(a[i][j] * x[j] for j in range(len(x))) for i in range(len(a))]


def resolver_lineal(a: Matriz, b: Vector) -> Vector:
    """Resuelve A·x = b por eliminación gaussiana con pivoteo parcial."""
    n = len(a)
    m = [row[:] + [b[i]] for i, row in enumerate(a)]   # matriz aumentada
    for col in range(n):
        piv = max(range(col, n), key=lambda r: abs(m[r][col]))
        if abs(m[piv][col]) < 1e-300:
            raise ValueError("Matriz singular: el modelo tiene un mecanismo (falta apoyo).")
        m[col], m[piv] = m[piv], m[col]
        pivote = m[col][col]
        for r in range(col + 1, n):
            factor = m[r][col] / pivote
            if factor == 0.0:
                continue
            for c in range(col, n + 1):
                m[r][c] -= factor * m[col][c]
    x = [0.0] * n
    for i in range(n - 1, -1, -1):
        s = m[i][n] - sum(m[i][j] * x[j] for j in range(i + 1, n))
        x[i] = s / m[i][i]
    return x


# --------------------------------------------------------------------------
# Geometría del elemento.
# --------------------------------------------------------------------------
def _cross(a: tuple[float, float, float], b: tuple[float, float, float]) -> tuple[float, float, float]:
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _norm(v: tuple[float, float, float]) -> tuple[float, float, float]:
    n = math.sqrt(v[0] ** 2 + v[1] ** 2 + v[2] ** 2)
    if n < 1e-300:
        raise ValueError("Vector nulo al normalizar.")
    return (v[0] / n, v[1] / n, v[2] / n)


def _dot(a, b) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def triada_local(ni, nj, v_ref: tuple[float, float, float]):
    """Triada ortonormal local (ex, ey, ez) y longitud L del elemento i→j."""
    dx, dy, dz = nj.x - ni.x, nj.y - ni.y, nj.z - ni.z
    L = math.sqrt(dx * dx + dy * dy + dz * dz)
    if L < 1e-300:
        raise ValueError("Elemento de longitud nula.")
    ex = (dx / L, dy / L, dz / L)
    ref = _norm(v_ref)
    if abs(_dot(ex, ref)) > 0.999:                      # ref casi paralelo al eje → elegir otro
        ref = (0.0, 1.0, 0.0) if abs(ex[2]) > 0.9 else (0.0, 0.0, 1.0)
    ey = _norm(_cross(ref, ex))
    ez = _cross(ex, ey)
    return ex, ey, ez, L


def rigidez_local(E: float, G: float, A: float, Iy: float, Iz: float, J: float, L: float) -> Matriz:
    """Matriz de rigidez local 12×12 de un elemento frame 3D.

    Orden de GDL por nodo: ux, uy, uz, rx, ry, rz (i = 0..5, j = 6..11).
    """
    k = _ceros(12, 12)
    ea = E * A / L
    gj = G * J / L
    z12, z6, z4, z2 = 12 * E * Iz / L ** 3, 6 * E * Iz / L ** 2, 4 * E * Iz / L, 2 * E * Iz / L
    y12, y6, y4, y2 = 12 * E * Iy / L ** 3, 6 * E * Iy / L ** 2, 4 * E * Iy / L, 2 * E * Iy / L

    # Axial (ux_i, ux_j).
    k[0][0] = ea;  k[0][6] = -ea;  k[6][6] = ea
    # Torsión (rx_i, rx_j).
    k[3][3] = gj;  k[3][9] = -gj;  k[9][9] = gj
    # Flexión plano x-y (uy, rz) — usa Iz.
    k[1][1] = z12; k[1][5] = z6;  k[1][7] = -z12; k[1][11] = z6
    k[5][5] = z4;  k[5][7] = -z6; k[5][11] = z2
    k[7][7] = z12; k[7][11] = -z6
    k[11][11] = z4
    # Flexión plano x-z (uz, ry) — usa Iy (signos opuestos al plano x-y).
    k[2][2] = y12; k[2][4] = -y6; k[2][8] = -y12; k[2][10] = -y6
    k[4][4] = y4;  k[4][8] = y6;  k[4][10] = y2
    k[8][8] = y12; k[8][10] = y6
    k[10][10] = y4
    # Simetrizar.
    for i in range(12):
        for j in range(i + 1, 12):
            k[j][i] = k[i][j]
    return k


def _transformacion_12(ex, ey, ez) -> Matriz:
    """Matriz T (12×12) global→local: 4 bloques diagonales de la rotación R (filas = ejes locales)."""
    R = [list(ex), list(ey), list(ez)]
    T = _ceros(12, 12)
    for blk in range(4):
        o = blk * 3
        for i in range(3):
            for j in range(3):
                T[o + i][o + j] = R[i][j]
    return T


@dataclass
class ResultadoAnalisis:
    """Resultado del análisis estático lineal."""
    desplazamientos: dict[int, tuple[float, ...]]   # nodo_id → (ux,uy,uz,rx,ry,rz)
    reacciones: dict[int, tuple[float, ...]]         # nodo_id (apoyado) → fuerzas/momentos
    n_gdl: int


def ensamblar_global(modelo: ModeloEstructural) -> Matriz:
    """Ensambla la matriz de rigidez global (n_gdl × n_gdl)."""
    idx = modelo.indice_nodos()
    mats = {m.id: m for m in modelo.materiales}
    secs = {s.id: s for s in modelo.secciones}
    nodos = {n.id: n for n in modelo.nodos}
    n = modelo.n_gdl
    K = _ceros(n, n)

    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        mat, sec = mats[e.material_id], secs[e.seccion_id]
        ex, ey, ez, L = triada_local(ni, nj, e.vector_referencia)
        kl = rigidez_local(mat.E, mat.G, sec.area, sec.inercia_y, sec.inercia_z, sec.constante_torsion, L)
        T = _transformacion_12(ex, ey, ez)
        kg = _matmul(_matmul(_transpuesta(T), kl), T)   # K_global = Tᵀ·K_local·T

        base = [idx[e.nodo_i] * GDL_POR_NODO, idx[e.nodo_j] * GDL_POR_NODO]
        mapa = [base[0] + d for d in range(6)] + [base[1] + d for d in range(6)]
        for a in range(12):
            for b in range(12):
                K[mapa[a]][mapa[b]] += kg[a][b]
    return K


def _vector_cargas(modelo: ModeloEstructural) -> Vector:
    idx = modelo.indice_nodos()
    F = [0.0] * modelo.n_gdl
    for c in modelo.cargas:
        base = idx[c.nodo_id] * GDL_POR_NODO
        for d, val in enumerate(c.componentes()):
            F[base + d] += val
    return F


def resolver(modelo: ModeloEstructural) -> ResultadoAnalisis:
    """Análisis estático lineal: ensambla K, aplica apoyos y resuelve K·u = F."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    idx = modelo.indice_nodos()
    n = modelo.n_gdl
    K = ensamblar_global(modelo)
    F = _vector_cargas(modelo)

    # GDL restringidos por los apoyos.
    fijos: set[int] = set()
    for ap in modelo.apoyos:
        base = idx[ap.nodo_id] * GDL_POR_NODO
        for d, restr in enumerate(ap.restricciones()):
            if restr:
                fijos.add(base + d)
    libres = [i for i in range(n) if i not in fijos]

    # Resolver el subsistema libre K_ff · u_f = F_f.
    Kff = [[K[i][j] for j in libres] for i in libres]
    Ff = [F[i] for i in libres]
    uf = resolver_lineal(Kff, Ff) if libres else []

    u = [0.0] * n
    for pos, i in enumerate(libres):
        u[i] = uf[pos]

    # Reacciones en los GDL fijos: R = K·u − F.
    Ku = _matvec(K, u)
    reac_full = [Ku[i] - F[i] for i in range(n)]

    desplazamientos = {
        nodo.id: tuple(u[idx[nodo.id] * GDL_POR_NODO + d] for d in range(6))
        for nodo in modelo.nodos
    }
    reacciones = {}
    for ap in modelo.apoyos:
        base = idx[ap.nodo_id] * GDL_POR_NODO
        reacciones[ap.nodo_id] = tuple(reac_full[base + d] for d in range(6))

    return ResultadoAnalisis(desplazamientos=desplazamientos, reacciones=reacciones, n_gdl=n)


@dataclass
class EsfuerzosElemento:
    """Fuerzas de extremo y esfuerzos internos de un elemento frame, en coordenadas locales."""
    elemento_id: int
    longitud: float
    extremo_i: tuple[float, float, float, float, float, float]   # (N, Vy, Vz, T, My, Mz) nodal en i
    extremo_j: tuple[float, float, float, float, float, float]   # idem en j

    @property
    def axial(self) -> float:
        """Fuerza axial de la barra (tracción +). N = −N_i = N_j."""
        return -self.extremo_i[0]

    def internos(self, t: float) -> tuple[float, float, float, float, float, float]:
        """Esfuerzos internos (N, Vy, Vz, T, My, Mz) en la estación local s = t·L (t ∈ [0,1]).

        Por equilibrio del segmento [0, s] (solo la fuerza nodal del extremo i actúa sobre él):
        N/Vy/Vz/T constantes; My, Mz lineales. N tracción +.
        """
        ni, vy, vz, ti, my, mz = self.extremo_i
        s = t * self.longitud
        return (-ni, -vy, -vz, -ti, -my - s * vz, -mz + s * vy)

    def diagrama(self, n: int = 11) -> list[tuple[float, ...]]:
        """n estaciones equiespaciadas: cada una (s, N, Vy, Vz, T, My, Mz), s de 0 a L."""
        if n < 2:
            raise ValueError("n debe ser ≥ 2.")
        return [(t * self.longitud, *self.internos(t)) for t in (k / (n - 1) for k in range(n))]


def esfuerzos_elementos(modelo: ModeloEstructural,
                        resultado: ResultadoAnalisis) -> dict[int, EsfuerzosElemento]:
    """Fuerzas de extremo y diagramas por elemento (coordenadas locales) del resultado del análisis.

    Post-procesamiento puro: por elemento, ``f_local = kl·(T·u)`` con los desplazamientos globales de
    sus dos nodos. Reusa la geometría/rigidez del ensamblaje; ``resolver`` ya validó el modelo.
    """
    nodos = {n.id: n for n in modelo.nodos}
    mats = {m.id: m for m in modelo.materiales}
    secs = {s.id: s for s in modelo.secciones}
    salida: dict[int, EsfuerzosElemento] = {}
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        mat, sec = mats[e.material_id], secs[e.seccion_id]
        ex, ey, ez, L = triada_local(ni, nj, e.vector_referencia)
        kl = rigidez_local(mat.E, mat.G, sec.area, sec.inercia_y, sec.inercia_z, sec.constante_torsion, L)
        T = _transformacion_12(ex, ey, ez)
        u_g = list(resultado.desplazamientos[e.nodo_i]) + list(resultado.desplazamientos[e.nodo_j])
        f_local = _matvec(kl, _matvec(T, u_g))
        salida[e.id] = EsfuerzosElemento(e.id, L, tuple(f_local[0:6]), tuple(f_local[6:12]))
    return salida

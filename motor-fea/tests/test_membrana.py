import math

import pytest

from motor_fea.core.membrana import (
    constitutiva_plana,
    esfuerzos_elemento,
    matvec,
    rigidez_membrana,
)
from motor_fea.core.solver import resolver_lineal


# Cuadrado unitario de referencia para varios tests.
_CUADRADO = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
_E, _NU, _T = 2.0e10, 0.2, 0.25


def test_constitutiva_plana_valores_cerrados():
    E, nu = 2.0e10, 0.2
    D = constitutiva_plana(E, nu)
    factor = E / (1.0 - nu * nu)
    assert D[0][0] == pytest.approx(factor)
    assert D[1][1] == pytest.approx(factor)
    assert D[0][1] == pytest.approx(factor * nu)
    assert D[1][0] == pytest.approx(factor * nu)
    assert D[2][2] == pytest.approx(factor * (1.0 - nu) / 2.0)
    assert D[0][2] == 0.0 and D[1][2] == 0.0
    assert D[2][0] == 0.0 and D[2][1] == 0.0


def test_rigidez_simetrica():
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    assert len(K) == 8 and all(len(fila) == 8 for fila in K)
    for i in range(8):
        for j in range(8):
            assert K[i][j] == pytest.approx(K[j][i], rel=1e-9, abs=1.0)


def test_modos_cuerpo_rigido_traslacion():
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    trans_x = [1.0, 0.0] * 4          # ux=1 en los 4 nodos
    trans_y = [0.0, 1.0] * 4          # uy=1 en los 4 nodos
    for u in (trans_x, trans_y):
        f = matvec(K, u)
        assert all(abs(fi) < 1e-3 for fi in f)


def test_modo_cuerpo_rigido_rotacion():
    # Rotación infinitesimal en torno al origen: u=-y, v=x → deformación nula.
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    u_rot = []
    for (x, y) in _CUADRADO:
        u_rot.extend([-y, x])
    f = matvec(K, u_rot)
    assert all(abs(fi) < 1e-3 for fi in f)


def test_rigidez_degenerada_lanza():
    horario = [(0.0, 0.0), (0.0, 1.0), (1.0, 1.0), (1.0, 0.0)]  # orden horario
    with pytest.raises(ValueError):
        rigidez_membrana(horario, _E, _NU, _T)


def test_patch_esfuerzo_constante():
    # Campo de desplazamiento lineal u=a+b·x+c·y, v=d+e·x+f·y → deformación
    # constante → esfuerzo constante exacto en el centro.
    b, c, e, f = 1e-4, 2e-5, -3e-5, 4e-5
    d_elem = []
    for (x, y) in _CUADRADO:
        d_elem.extend([0.001 + b * x + c * y, -0.002 + e * x + f * y])
    eps = [b, f, c + e]                      # [εxx, εyy, γxy]
    D = constitutiva_plana(_E, _NU)
    esperado = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, d_elem, 0.0, 0.0)
    assert sxx == pytest.approx(esperado[0], rel=1e-6)
    assert syy == pytest.approx(esperado[1], rel=1e-6)
    assert txy == pytest.approx(esperado[2], rel=1e-6)


def test_cortante_puro():
    # u = gamma·y, v = 0 → γxy = gamma, εxx = εyy = 0 → τxy = G·gamma.
    gamma = 1e-4
    d_elem = []
    for (x, y) in _CUADRADO:
        d_elem.extend([gamma * y, 0.0])
    G = _E / (2.0 * (1.0 + _NU))
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, d_elem, 0.0, 0.0)
    assert sxx == pytest.approx(0.0, abs=1.0)
    assert syy == pytest.approx(0.0, abs=1.0)
    assert txy == pytest.approx(G * gamma, rel=1e-6)


def test_traccion_uniaxial_resuelta():
    # Cuadrado L×L×t. Apoyos: nodo0 (ux,uy)=0, nodo3 ux=0 (permite Poisson).
    # Carga P/2 en +x sobre los nodos 1 y 2 (borde derecho).
    # Q4 reproduce exacto el estado de esfuerzo constante: σxx = P/(L·t).
    L, P = 1.0, 1.0e6
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    # GDL globales = locales (un solo elemento). Índices: nodo n → 2n (ux), 2n+1 (uy).
    fijos = {0, 1, 6}                        # ux0, uy0, ux3
    libres = [d for d in range(8) if d not in fijos]
    F = [0.0] * 8
    F[2] += P / 2.0                          # ux nodo1
    F[4] += P / 2.0                          # ux nodo2
    Kff = [[K[i][j] for j in libres] for i in libres]
    Ff = [F[i] for i in libres]
    uf = resolver_lineal(Kff, Ff)
    u = [0.0] * 8
    for pos, dgl in enumerate(libres):
        u[dgl] = uf[pos]
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, u, 0.0, 0.0)
    assert sxx == pytest.approx(P / (L * _T), rel=1e-6)
    assert syy == pytest.approx(0.0, abs=P / (L * _T) * 1e-6)
    assert txy == pytest.approx(0.0, abs=P / (L * _T) * 1e-6)
    # Alargamiento del borde derecho: εxx·L = (σxx/E)·L.
    assert u[2] == pytest.approx((P / (L * _T)) / _E * L, rel=1e-6)


def test_rango_sin_modos_espurios():
    """K tiene exactamente 3 modos de cuerpo rígido (espacio nulo de dim=3).

    Estrategia: fijar los 3 GDL que eliminan los modos rígidos (ux0, uy0, ux3),
    reducir a la submatriz 5×5 Kff y verificar que sea definida positiva mediante
    eliminación de Gauss (todos los pivotes > umbral).  Un modo espurio (hourglass,
    etc.) haría que Kff fuese singular (pivote ≈ 0).
    También se confirma que los 3 modos rígidos conocidos dan energía ≈ 0.
    """
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)

    # --- 1. Energía de los 3 modos rígidos debe ser ≈ 0 ---
    trans_x = [1.0, 0.0] * 4
    trans_y = [0.0, 1.0] * 4
    u_rot = []
    for (x, y) in _CUADRADO:
        u_rot.extend([-y, x])
    for u in (trans_x, trans_y, u_rot):
        Ku = matvec(K, u)
        energia = sum(u[i] * Ku[i] for i in range(8))
        assert abs(energia) < 1e-3, f"Modo rígido con energía no nula: {energia}"

    # --- 2. Kff 5×5 definida positiva → cero modos espurios ---
    fijos = {0, 1, 6}           # ux0, uy0, ux3 (GDL que fijan los 3 modos rígidos)
    libres = [d for d in range(8) if d not in fijos]  # [2,3,4,5,7]
    n = len(libres)             # 5
    Kff = [[K[libres[i]][libres[j]] for j in range(n)] for i in range(n)]

    def _cholesky_pivots(A: list[list[float]]) -> list[float]:
        """Devuelve los pivotes de la eliminación de Gauss con pivoteo parcial."""
        import copy
        M = [row[:] for row in A]  # copia
        pivots = []
        for col in range(len(M)):
            # Pivoteo parcial (mayor valor absoluto en la columna)
            max_row = max(range(col, len(M)), key=lambda r: abs(M[r][col]))
            M[col], M[max_row] = M[max_row], M[col]
            pivot = M[col][col]
            pivots.append(pivot)
            if abs(pivot) < 1e-20:
                continue  # singularidad, no dividir entre cero
            for row in range(col + 1, len(M)):
                factor = M[row][col] / pivot
                for k in range(col, len(M)):
                    M[row][k] -= factor * M[col][k]
        return pivots

    pivots = _cholesky_pivots(Kff)
    diag_max = max(K[i][i] for i in range(8))
    umbral = diag_max * 1e-8     # umbral relativo holgado

    for idx, piv in enumerate(pivots):
        assert piv > umbral, (
            f"Pivote {idx} = {piv:.3e} <= umbral {umbral:.3e}: "
            f"Kff singular → posible modo espurio")


def test_esfuerzos_fuera_del_centro():
    """El campo de deformación constante reproduce el mismo esfuerzo en CUALQUIER (ξ,η).

    Reutiliza el campo del patch test con (ξ,η)=(0.6, -0.3) en lugar del centro.
    Para un campo lineal (deformación constante), el Q4 bilineal debe recuperar
    exactamente σ = D·ε independientemente del punto de muestreo.
    """
    b, c, e, f = 1e-4, 2e-5, -3e-5, 4e-5
    d_elem = []
    for (x, y) in _CUADRADO:
        d_elem.extend([0.001 + b * x + c * y, -0.002 + e * x + f * y])
    eps = [b, f, c + e]
    D = constitutiva_plana(_E, _NU)
    esperado = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, d_elem, 0.6, -0.3)
    assert sxx == pytest.approx(esperado[0], rel=1e-6)
    assert syy == pytest.approx(esperado[1], rel=1e-6)
    assert txy == pytest.approx(esperado[2], rel=1e-6)


def test_esfuerzos_longitud_invalida():
    """esfuerzos_elemento lanza ValueError si d_elem no tiene exactamente 8 GDL."""
    d_corto = [0.0] * 6
    with pytest.raises(ValueError):
        esfuerzos_elemento(_CUADRADO, _E, _NU, d_corto)


def test_cuadrilatero_no_rectangular_patch():
    # Trapecio (jacobiano variable). K simétrica + patch de esfuerzo constante.
    trapecio = [(0.0, 0.0), (2.0, 0.0), (1.5, 1.0), (0.2, 1.0)]
    K = rigidez_membrana(trapecio, _E, _NU, _T)
    for i in range(8):
        for j in range(8):
            assert K[i][j] == pytest.approx(K[j][i], rel=1e-9, abs=1.0)
    b, c, e, f = 1e-4, 2e-5, -3e-5, 4e-5
    d_elem = []
    for (x, y) in trapecio:
        d_elem.extend([0.001 + b * x + c * y, -0.002 + e * x + f * y])
    eps = [b, f, c + e]
    D = constitutiva_plana(_E, _NU)
    esperado = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    sxx, syy, txy = esfuerzos_elemento(trapecio, _E, _NU, d_elem, 0.0, 0.0)
    assert sxx == pytest.approx(esperado[0], rel=1e-6)
    assert syy == pytest.approx(esperado[1], rel=1e-6)
    assert txy == pytest.approx(esperado[2], rel=1e-6)

"""Validación del elemento de placa ACM (sin convergencia de malla todavía).

Pruebas del elemento aislado: simetría, diagonal positiva y los 3 modos de
cuerpo rígido (traslación + 2 rotaciones) deben dar energía/fuerza nula —
``K·d_rígido ≈ 0``. Es la verificación limpia y exacta de la formulación antes
de ensamblar mallas (la convergencia vs placa simplemente apoyada viene luego).
"""
from motor_fea.core.placa import matvec, rigidez_placa

LX, LY = 1.0, 1.0
E, NU, T = 2.0e10, 0.2, 0.2
K = rigidez_placa(LX, LY, E, NU, T)
ESCALA = max(abs(K[i][j]) for i in range(12) for j in range(12))


def _casi_cero(v):
    return max(abs(x) for x in v) < 1e-6 * ESCALA


def test_simetrica():
    for i in range(12):
        for j in range(12):
            assert abs(K[i][j] - K[j][i]) < 1e-6 * ESCALA


def test_diagonal_positiva():
    for i in range(12):
        assert K[i][i] > 0.0


def test_modo_cuerpo_rigido_traslacion():
    # w = 1, θ = 0 en los 4 nodos.
    d = [1, 0, 0] * 4
    assert _casi_cero(matvec(K, d))


def test_modo_cuerpo_rigido_rotacion_x():
    # w = y, θx = ∂w/∂y = 1, θy = 0.  Nodos y = 0,0,ly,ly.
    d = [0, 1, 0, 0, 1, 0, LY, 1, 0, LY, 1, 0]
    assert _casi_cero(matvec(K, d))


def test_modo_cuerpo_rigido_rotacion_y():
    # w = x, θx = 0, θy = −∂w/∂x = −1.  Nodos x = 0,lx,lx,0.
    d = [0, 0, -1, LX, 0, -1, LX, 0, -1, 0, 0, -1]
    assert _casi_cero(matvec(K, d))


def test_solo_tres_modos_nulos():
    # Un patrón NO rígido (curvatura) debe dar energía > 0.
    d = [0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0]   # w=1 en un solo nodo
    energia = sum(d[i] * matvec(K, d)[i] for i in range(12))
    assert energia > 1e-3 * ESCALA


def test_rigidez_escala_con_cubo_del_espesor():
    # D ∝ t³ → K ∝ t³.
    K2 = rigidez_placa(LX, LY, E, NU, 2 * T)
    assert abs(K2[0][0] / K[0][0] - 8.0) < 1e-9

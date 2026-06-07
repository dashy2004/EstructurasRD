"""Tests de las combinaciones de carga LRFD (ACI 318-19 §5.3.1)."""
from motor_fea.normativa import combinaciones as cb


def test_combo_1_solo_muerta():
    c = cb.combinaciones_resistencia(D=10.0)
    assert abs(c["1"] - 1.4 * 10.0) < 1e-12


def test_combo_2_gravedad():
    c = cb.combinaciones_resistencia(D=10.0, L=5.0, Lr=2.0)
    # 1.2·10 + 1.6·5 + 0.5·max(Lr,S,R)=0.5·2
    assert abs(c["2"] - (1.2 * 10 + 1.6 * 5 + 0.5 * 2)) < 1e-12


def test_roof_toma_el_mayor_de_Lr_S_R():
    c = cb.combinaciones_resistencia(D=0.0, S=7.0, Lr=2.0, R=3.0)
    # término roof en combo 2 usa max(2,7,3)=7
    assert abs(c["2"] - (0.5 * 7.0)) < 1e-12


def test_viento_genera_mas_y_menos():
    c = cb.combinaciones_resistencia(D=10.0, W=8.0)
    # combo 6: 0.9·10 ± 1.0·8 = {17, 1}
    assert abs(c["6(+)"] - (9.0 + 8.0)) < 1e-12
    assert abs(c["6(-)"] - (9.0 - 8.0)) < 1e-12


def test_sismo_reversible():
    c = cb.combinaciones_resistencia(D=10.0, E=6.0)
    assert abs(c["7(+)"] - (9.0 + 6.0)) < 1e-12
    assert abs(c["7(-)"] - (9.0 - 6.0)) < 1e-12


def test_envolvente_captura_uplift():
    env = cb.envolvente(D=10.0, W=20.0)
    # 0.9·10 − 1.0·20 = −11 (levantamiento) es el mínimo.
    assert abs(env.minimo - (9.0 - 20.0)) < 1e-9
    assert env.minimo < 0
    assert env.combo_min == "6(-)"
    assert env.maximo >= env.minimo


def test_numero_de_combinaciones_expandidas():
    c = cb.combinaciones_resistencia(D=1, L=1, Lr=1, S=1, W=1, E=1)
    # combos con W o E (3b,4,6 con W; 5,7 con E) se duplican a ±.
    # 1,2,3a (sin reversibles)=3 ; 3b,4,6,5,7 (con reversibles)=5·2=10 → 13.
    assert len(c) == 13

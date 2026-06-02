"""Tests de la verificación de vigas ACI 318-19 (N, mm, MPa)."""
import math

from motor_fea.normativa import aci318

# Ejemplo de referencia: b=300, d=540 mm; f'c=28, fy=420 MPa.
B, D, FC, FY = 300.0, 540.0, 28.0, 420.0


def test_as_requerido_consistencia_phiMn_igual_Mu():
    # Verificación interna: el As requerido para un Mu debe reproducir φMn ≈ Mu.
    mu = 200e6  # N·mm = 200 kN·m
    as_req, insuf = aci318.as_requerido_flexion(mu, B, D, FC, FY)
    assert not insuf
    phi_mn = aci318.momento_resistente(as_req, B, D, FC, FY)
    assert abs(phi_mn - mu) / mu < 1e-6


def test_as_requerido_valor_calculado_a_mano():
    as_req, _ = aci318.as_requerido_flexion(200e6, B, D, FC, FY)
    assert abs(as_req - 1038.56) < 0.5      # mm², calculado a mano


def test_as_minimo_flexion():
    # max(0.25√28/420, 1.4/420)·300·540
    rho = max(0.25 * math.sqrt(28) / 420, 1.4 / 420)
    assert abs(aci318.as_minimo_flexion(B, D, FC, FY) - rho * B * D) < 1e-6


def test_seccion_insuficiente_devuelve_nan():
    as_req, insuf = aci318.as_requerido_flexion(5000e6, B, D, FC, FY)
    assert insuf and math.isnan(as_req)


def test_verificar_flexion_cumple_y_no_cumple():
    # As provisto generoso → cumple; As escaso → no cumple.
    ok = aci318.verificar_viga_flexion(200e6, B, D, FC, FY, as_provisto=1500.0)
    assert ok.cumple and ok.ratio < 1.0
    no = aci318.verificar_viga_flexion(200e6, B, D, FC, FY, as_provisto=600.0)
    assert not no.cumple


def test_cortante_concreto_valor():
    vc = aci318.cortante_concreto(B, D, FC)
    # 0.17·√28·300·540
    assert abs(vc - 0.17 * math.sqrt(28) * B * D) < 1e-6
    assert abs(vc - 145729) < 50            # ~145.7 kN


def test_cortante_acero_y_resistencia():
    vs = aci318.cortante_acero(av=142.0, fyt=420.0, d=D, s=150.0)   # 2 ramas #10
    assert abs(vs - 142.0 * 420.0 * D / 150.0) < 1e-6
    r = aci318.verificar_viga_cortante(150e3, B, D, FC, av=142.0, fyt=420.0, s=150.0)
    assert r.phi_vn == 0.75 * (r.vc + r.vs)
    assert r.cumple == (r.ratio <= 1.0 + 1e-9 and not r.vs_excede_maximo)


def test_cortante_detecta_Vs_excesivo():
    # Estribos muy juntos → Vs por encima del máximo (§22.5.1.2).
    r = aci318.verificar_viga_cortante(150e3, B, D, FC, av=400.0, fyt=420.0, s=40.0)
    assert r.vs_excede_maximo
    assert not r.cumple


# --------------------------- Columnas P-M ---------------------------
# Columna 400×400, f'c=28, fy=420; refuerzo en 2 capas (cubierta ~60 mm).
CB, CH = 400.0, 400.0
CAPAS = [(60.0, 1020.0), (340.0, 1020.0)]   # As ≈ 2#8 por capa


def test_beta1_segun_fc():
    assert aci318.beta1(28) == 0.85
    assert abs(aci318.beta1(35) - 0.80) < 1e-9
    assert abs(aci318.beta1(56) - 0.65) < 1e-9     # 0.85 - 0.05·(56-28)/7 = 0.65
    assert aci318.beta1(70) == 0.65                # piso


def test_phi_por_deformacion_transicion():
    ey = 420.0 / 200000.0
    assert aci318.phi_por_deformacion(ey - 1e-4, 420.0) == 0.65       # compresión
    assert aci318.phi_por_deformacion(ey + 0.003 + 1e-4, 420.0) == 0.90  # tracción
    mid = aci318.phi_por_deformacion(ey + 0.0015, 420.0)             # mitad de transición
    assert 0.65 < mid < 0.90


def test_axial_pura_calculada_a_mano():
    ag = CB * CH                 # 160000 mm²
    ast = 2 * 1020.0             # 2040 mm²
    po = aci318.axial_pura_nominal(ag, ast, FC, 420.0)
    # 0.85·28·(160000−2040) + 420·2040
    assert abs(po - (0.85 * 28 * (ag - ast) + 420.0 * ast)) < 1e-3
    # φPn,max estribos = 0.80·0.65·Po
    assert abs(aci318.axial_maxima_diseno(ag, ast, FC, 420.0) - 0.80 * 0.65 * po) < 1e-3


def test_punto_balanceado_et_igual_ey():
    cb = aci318.profundidad_balanceada(d=340.0, fy=420.0)
    p = aci318.punto_interaccion(cb, CB, CH, FC, 420.0, CAPAS)
    ey = 420.0 / 200000.0
    assert abs(p.et - ey) < 1e-6              # por definición del punto balanceado
    assert abs(p.phi - 0.65) < 1e-6           # justo en el inicio de la transición
    assert p.pn > 0 and p.mn > 0


def test_interaccion_pn_decrece_al_bajar_c():
    p_alto = aci318.punto_interaccion(380.0, CB, CH, FC, 420.0, CAPAS)
    p_bajo = aci318.punto_interaccion(120.0, CB, CH, FC, 420.0, CAPAS)
    assert p_alto.pn > p_bajo.pn              # más compresión con eje neutro profundo
    assert p_bajo.et > p_alto.et             # más tracción con c chico


def test_interaccion_phi_pn_y_phi_mn():
    p = aci318.punto_interaccion(200.0, CB, CH, FC, 420.0, CAPAS)
    assert abs(p.phi_pn - p.phi * p.pn) < 1e-9
    assert abs(p.phi_mn - p.phi * p.mn) < 1e-9


# --------------------- Zapatas: punzonamiento (§22.6) ---------------------
def test_perimetro_critico_interior_y_esquina():
    assert aci318.perimetro_critico(400, 400, 440, "interior") == 2 * 840 + 2 * 840  # 3360
    assert aci318.perimetro_critico(400, 400, 440, "esquina") == (400 + 220) + (400 + 220)  # 1240


def test_punzonamiento_columna_cuadrada_gobierna_termino_base():
    r = aci318.cortante_punzonamiento(1.5e6, 400, 400, 440, FC, "interior")
    assert abs(r.bo - 3360.0) < 1e-6
    assert r.beta_c == 1.0
    # cuadrada → el término base 0.33√f'c gobierna
    assert abs(r.vc_esfuerzo - 0.33 * math.sqrt(FC)) < 1e-6
    assert abs(r.vc - r.vc_esfuerzo * r.bo * 440) < 1e-3
    assert abs(r.phi_vc - 0.75 * r.vc) < 1e-3


def test_punzonamiento_columna_alargada_gobierna_beta_c():
    r = aci318.cortante_punzonamiento(1.0e6, 800, 200, 300, FC, "interior")
    assert r.beta_c == 4.0
    # βc grande → el término 0.17(1+2/βc)√f'c es el menor
    assert r.vc_esfuerzo == min(r.vc_terminos)
    assert abs(r.vc_esfuerzo - r.vc_terminos[1]) < 1e-9


def test_punzonamiento_esquina_alpha_s_20_y_cumple_flag():
    r = aci318.cortante_punzonamiento(200e3, 400, 400, 300, FC, "esquina")
    assert r.alpha_s == 20.0
    # ratio y cumple coherentes
    assert r.cumple == (r.ratio <= 1.0 + 1e-9)
    # carga muy alta → no cumple
    r2 = aci318.cortante_punzonamiento(5.0e6, 400, 400, 300, FC, "esquina")
    assert not r2.cumple and r2.ratio > 1.0


# --------------------- Losas: diseño a flexión ---------------------
# Franja de 1 m: b=1000, h=200, d=170 mm; f'c=21, fy=420 MPa.
LB, LH, LD, LFC, LFY = 1000.0, 200.0, 170.0, 21.0, 420.0


def test_diseno_losa_consistencia_phiMn():
    d = aci318.diseno_losa_franja(50e6, LB, LH, LD, LFC, LFY)   # 50 kN·m/m
    assert not d.seccion_insuficiente
    phi_mn = aci318.momento_resistente(d.as_requerido, LB, LD, LFC, LFY)
    assert abs(phi_mn - 50e6) / 50e6 < 1e-6


def test_as_minimo_temperatura_grado_420():
    # ρ=0.0018 · b · h
    assert abs(aci318.as_minimo_temperatura(LB, LH, LFY) - 0.0018 * LB * LH) < 1e-9
    # Grado 280 → ρ=0.0020
    assert abs(aci318.as_minimo_temperatura(LB, LH, 280.0) - 0.0020 * LB * LH) < 1e-9


def test_espaciamiento_maximo_losa():
    assert aci318.espaciamiento_maximo_losa(200) == 400.0     # min(2·200,450)
    assert aci318.espaciamiento_maximo_losa(250) == 450.0     # tope 450


def test_diseno_losa_minimo_gobierna_con_momento_bajo():
    d = aci318.diseno_losa_franja(5e6, LB, LH, LD, LFC, LFY)  # momento bajo
    assert d.gobierna_minimo
    assert abs(d.as_diseno - aci318.as_minimo_temperatura(LB, LH, LFY)) < 1e-6
    assert "#" in d.armadura.disponer and "mm" in d.armadura.disponer


def test_diseno_losa_selecciona_barra_y_cumple():
    d = aci318.diseno_losa_franja(50e6, LB, LH, LD, LFC, LFY)
    assert d.armadura.cumple
    assert d.armadura.as_provista >= d.as_diseno
    assert d.armadura.espaciamiento <= aci318.espaciamiento_maximo_losa(LH)


def test_diseno_losa_seccion_insuficiente():
    d = aci318.diseno_losa_franja(900e6, LB, 100.0, 70.0, LFC, LFY)
    assert d.seccion_insuficiente
    assert d.armadura.disponer == "SECCIÓN INSUFICIENTE"

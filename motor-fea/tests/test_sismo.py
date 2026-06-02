"""Tests del espectro R-001 y del cortante basal sísmico (3 capas juntas)."""
import math

from motor_fea import sismo
from motor_fea.normativa import r001
from motor_fea.normativa.r001 import ZonaSismica

from modelos_ref import voladizo   # constructor de modelo compartido

# Zona I, sitio fa=1.0, fv=1.5 → SDS=2/3·1.55, SD1=2/3·1.5·0.75, T0=0.2·SD1/SDS, Ts=5·T0.
ZONA, FA, FV = ZonaSismica.ZONA_I, 1.0, 1.5
_P = r001.espectro_diseno(ZONA, FA, FV)
SDS, SD1, T0, TS = _P["SDS"], _P["SD1"], _P["T0"], _P["Ts"]


def test_espectro_meseta_devuelve_SDS():
    t = 0.5 * (T0 + TS)                       # dentro de la meseta
    assert abs(r001.aceleracion_espectral(ZONA, FA, FV, t) - SDS) < 1e-12


def test_espectro_rama_larga_es_SD1_sobre_T():
    t = 2.0                                   # T > Ts
    assert abs(r001.aceleracion_espectral(ZONA, FA, FV, t) - SD1 / t) < 1e-12


def test_espectro_rampa_corta():
    t = 0.5 * T0                              # T < T0
    esperado = SDS * (0.4 + 0.6 * t / T0)
    assert abs(r001.aceleracion_espectral(ZONA, FA, FV, t) - esperado) < 1e-12


def test_espectro_continuo_en_los_quiebres():
    # En T0 la rampa alcanza SDS; en Ts la meseta empalma con SD1/T.
    assert abs(r001.aceleracion_espectral(ZONA, FA, FV, T0) - SDS) < 1e-9
    assert abs(r001.aceleracion_espectral(ZONA, FA, FV, TS) - SD1 / TS) < 1e-9


def test_cortante_basal_sismico_voladizo():
    masas = {2: 1000.0}
    r = sismo.cortante_basal_sismico(voladizo(), masas, ZONA, FA, FV, rd=5.5, u=1.0)
    # El voladizo tiene T≈0.162 s (en la meseta) → Sa = SDS.
    assert T0 <= r.periodo <= TS
    assert abs(r.sa - SDS) < 1e-9
    assert abs(r.cb - SDS / 5.5) < 1e-9       # Cb = U·Sa/Rd, por encima del piso 0.03
    assert abs(r.peso - 1000.0 * 9.81) < 1e-6
    assert abs(r.cortante_basal - r.cb * r.peso) < 1e-6


def test_piso_de_cortante_basal_0_03():
    # Rd enorme → Cb cae al piso de 0.03 (R-001).
    r = sismo.cortante_basal_sismico(voladizo(), {2: 1000.0}, ZONA, FA, FV, rd=1000.0)
    assert abs(r.cb - 0.03) < 1e-12


# ---- Análisis modal-espectral (modal + espectro + CQC) sobre la cadena 2 GDL ----
from modelos_ref import M_MASA, cadena_2gdl   # noqa: E402

_MASAS_CADENA = {1: M_MASA, 2: M_MASA}


def test_modal_espectral_captura_toda_la_masa():
    r = sismo.cortante_basal_modal_espectral(cadena_2gdl(), _MASAS_CADENA, ZONA, FA, FV,
                                             rd=5.5, direccion="x", n_modos=2)
    # 2 modos en una cadena de 2 GDL → 100% de la masa (≥90% requerido por norma).
    assert r.masa_participativa / (2 * M_MASA) > 0.90
    assert r.cortante_basal > 0
    assert len(r.modos) == 2


def test_modal_espectral_modo_fundamental_domina():
    r = sismo.cortante_basal_modal_espectral(cadena_2gdl(), _MASAS_CADENA, ZONA, FA, FV,
                                             rd=5.5, direccion="x", n_modos=2)
    m1, m2 = r.modos
    assert m1.masa_efectiva > m2.masa_efectiva       # el 1er modo capta más masa
    assert m1.cortante > m2.cortante


def test_modal_espectral_menor_o_igual_que_estatico():
    est = sismo.cortante_basal_sismico(cadena_2gdl(), _MASAS_CADENA, ZONA, FA, FV, rd=5.5)
    mod = sismo.cortante_basal_modal_espectral(cadena_2gdl(), _MASAS_CADENA, ZONA, FA, FV,
                                               rd=5.5, direccion="x", n_modos=2)
    # El modal-espectral reparte la masa por modo y combina con CQC → ≤ estático.
    assert mod.cortante_basal <= est.cortante_basal * 1.001
    assert mod.cortante_basal > 0.85 * est.cortante_basal

"""Cortante basal sísmico — módulo de composición (caso de uso).

Vive *por encima* de las capas: orquesta el análisis (``core.modal`` → período
fundamental) con la normativa (``normativa.r001`` → espectro + cortante basal)
para producir el cortante basal estático equivalente de R-001. Así ``core`` y
``normativa`` siguen sin conocerse; la dependencia fluye en una sola dirección.

Flujo: modelo + masas → T (modal) → Sa(T) (espectro R-001) → Cb = max(U·Sa/Rd,
0.03) → V = Cb·W, con W = Σ masas · g.
"""
from __future__ import annotations

from dataclasses import dataclass

from motor_fea.core.combinacion_modal import cqc
from motor_fea.core.modal import participacion_modal, periodo_fundamental
from motor_fea.core.modelo import ModeloEstructural
from motor_fea.normativa import r001

GRAVEDAD = 9.81   # m/s²


@dataclass
class ResultadoSismico:
    """Resultado del cortante basal estático equivalente (R-001)."""
    periodo: float       # T fundamental (s)
    sa: float            # aceleración espectral de diseño (g)
    cb: float            # coeficiente de cortante basal
    peso: float          # W = Σ masas · g (N)
    cortante_basal: float  # V = Cb·W (N)


def cortante_basal_sismico(modelo: ModeloEstructural, masas: dict[int, float],
                           zona: r001.ZonaSismica, fa: float, fv: float,
                           rd: float, u: float = 1.0, g: float = GRAVEDAD) -> ResultadoSismico:
    """Cortante basal estático equivalente de R-001 para ``modelo`` con ``masas`` (nodo→kg).

    Args:
        zona: zona sísmica (I o II).
        fa, fv: factores de sitio.
        rd: factor de reducción de respuesta (TABLA 8 de R-001).
        u: factor de importancia (Grupo I=1.50 … V=0.90). Default 1.0.
        g: aceleración de la gravedad (m/s²).
    """
    modal = periodo_fundamental(modelo, masas)
    sa = r001.aceleracion_espectral(zona, fa, fv, modal.periodo)
    cb = r001.cortante_basal(u, sa, rd)
    peso = sum(masas.values()) * g
    return ResultadoSismico(
        periodo=modal.periodo,
        sa=sa,
        cb=cb,
        peso=peso,
        cortante_basal=cb * peso,
    )


@dataclass
class ModoEspectral:
    """Aporte de un modo al cortante basal dinámico."""
    periodo: float       # Ti (s)
    sa: float            # Sa(Ti) (g)
    masa_efectiva: float # M_eff_i (kg) en la dirección
    cortante: float      # Vi = (U·Sa/Rd)·M_eff·g (N)


@dataclass
class ResultadoModalEspectral:
    """Cortante basal por análisis modal-espectral (modal + espectro + CQC)."""
    cortante_basal: float          # V combinado (N)
    masa_participativa: float      # Σ M_eff (kg) — para verificar ≥90% de la masa
    modos: list[ModoEspectral]


def cortante_basal_modal_espectral(modelo: ModeloEstructural, masas: dict[int, float],
                                   zona: r001.ZonaSismica, fa: float, fv: float, rd: float,
                                   direccion: str = "x", u: float = 1.0, n_modos: int = 3,
                                   zeta: float = 0.05, g: float = GRAVEDAD) -> ResultadoModalEspectral:
    """Cortante basal dinámico: por modo Sa(Ti)·M_eff_i, combinado con CQC.

    Más riguroso que el estático: reparte la masa entre modos según su
    participación en ``direccion`` y combina sus máximos con CQC (la correlación
    de frecuencias cercanas). ``n_modos`` = número de modos a considerar.
    """
    part = participacion_modal(modelo, masas, direccion, n_modos)
    modos: list[ModoEspectral] = []
    cortantes: list[float] = []
    omegas: list[float] = []
    for modal, p in part:
        sa = r001.aceleracion_espectral(zona, fa, fv, modal.periodo)
        cs = u * sa / rd
        vi = cs * p.masa_efectiva * g
        modos.append(ModoEspectral(modal.periodo, sa, p.masa_efectiva, vi))
        cortantes.append(vi)
        omegas.append(modal.omega)
    v = cqc(cortantes, omegas, zeta) if cortantes else 0.0
    return ResultadoModalEspectral(
        cortante_basal=v,
        masa_participativa=sum(m.masa_efectiva for m in modos),
        modos=modos,
    )

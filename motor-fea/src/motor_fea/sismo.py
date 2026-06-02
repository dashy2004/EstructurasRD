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

from motor_fea.core.modal import periodo_fundamental
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

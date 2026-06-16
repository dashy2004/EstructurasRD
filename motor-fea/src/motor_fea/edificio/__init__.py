"""Modelo canónico del edificio (capa de autoría EstructurasRD).

Fuente de verdad del contrato de autoría: de aquí lo consumen el FEA (vía
síntesis, rebanada siguiente), el visor y la memoria. Distinto del
``ModeloEstructural`` de ``motor_fea.core`` (malla FEA de bajo nivel).
"""
from motor_fea.edificio.contrato import (
    VERSION_CONTRATO,
    proyecto_a_dict,
    proyecto_a_json,
    proyecto_desde_dict,
    proyecto_desde_json,
)
from motor_fea.edificio.modelo import (
    CargasGlobales,
    CargasLosa,
    Columna,
    Edificio,
    Losa,
    Metadata,
    Muro,
    Nivel,
    Proyecto,
    TIPOS_LOSA,
    Zapata,
)
from motor_fea.edificio.sintesis import (  # noqa: E402
    material_a_E_pa,
    sintetizar,
)

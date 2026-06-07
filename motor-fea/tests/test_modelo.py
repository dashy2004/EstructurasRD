"""Tests para motor_fea.core.modelo (dataclasses de datos)."""


def test_carga_nodal_caso_default_y_validacion():
    from motor_fea.core.modelo import CargaNodal, ModeloEstructural, Nodo
    assert CargaNodal(1).caso == "D"                      # default retrocompatible
    m = ModeloEstructural()
    m.nodos.append(Nodo(1, 0.0, 0.0, 0.0))
    m.cargas.append(CargaNodal(1, fz=-1000.0, caso="ZZ"))
    assert any("ZZ" in e for e in m.validar())            # caso inválido reportado
    m.cargas[-1] = CargaNodal(1, fz=-1000.0, caso="L")
    assert not any("caso" in e.lower() for e in m.validar())

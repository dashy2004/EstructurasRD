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


def test_losas_no_afectan_el_analisis():
    from motor_fea.core.modelo import (
        ModeloEstructural, Nodo, Material, Seccion, ElementoFrame, Apoyo, LosaViz,
    )
    m = ModeloEstructural()
    m.nodos.extend([Nodo(1, 0.0, 0.0, 0.0), Nodo(2, 3.0, 0.0, 0.0)])
    m.materiales.append(Material(1, 2.0e10, 0.2, 2400.0))
    m.secciones.append(Seccion(1, 0.09, 0.000675, 0.000675, 0.00114))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1, (0.0, 0.0, 1.0)))
    m.apoyos.append(Apoyo(1, True, True, True, True, True, True))

    n_gdl_antes = m.n_gdl
    assert m.validar() == []
    m.losas.append(LosaViz(1, [[0.0, 0.0, 0.0], [3.0, 0.0, 0.0], [3.0, 3.0, 0.0], [0.0, 3.0, 0.0]]))
    assert m.n_gdl == n_gdl_antes      # las losas no cambian los GDL
    assert m.validar() == []           # ni la validez del modelo

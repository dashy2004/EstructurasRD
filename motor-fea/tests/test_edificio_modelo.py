"""Tests del modelo canónico del edificio (Rebanada A)."""


def test_losa_construccion_y_defaults():
    from motor_fea.edificio.modelo import CargasLosa, Losa, TIPOS_LOSA

    assert "maciza" in TIPOS_LOSA
    l = Losa(id=1, tipo="maciza", espesor=0.20,
             puntos=((0, 0), (5, 0), (5, 5), (0, 5)))
    assert l.cargas == CargasLosa(0.0, 0.0)          # cargas por defecto en cero
    l2 = Losa(id=2, tipo="aligerada", espesor=0.25,
              puntos=((0, 0), (5, 0), (5, 5)),
              cargas=CargasLosa(muerta=1.5, viva=2.0))
    assert (l2.cargas.muerta, l2.cargas.viva) == (1.5, 2.0)

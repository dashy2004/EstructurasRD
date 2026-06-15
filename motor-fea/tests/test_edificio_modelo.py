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


def test_nivel_propaga_cota_a_sus_losas():
    from motor_fea.edificio.modelo import Losa, Nivel

    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)))
    nivel = Nivel(id=1, nombre="Primer nivel", cota=3.0, losas=(losa,))

    pts3d = nivel.puntos_losa_3d(losa)
    assert all(z == 3.0 for (_x, _y, z) in pts3d)     # la cota del nivel baja a la losa
    assert pts3d[0] == [0, 0, 3.0]


def test_nombre_del_nivel_es_independiente_de_la_losa():
    from motor_fea.edificio.modelo import Losa, Nivel

    losa = Losa(id=7, tipo="maciza", espesor=0.20, puntos=((0, 0), (1, 0), (1, 1)))
    nivel = Nivel(id=1, nombre="Mezzanine", cota=0.0, losas=(losa,))
    assert nivel.nombre == "Mezzanine"                # no derivado de la losa/sistema
    assert not hasattr(losa, "nombre")                # la losa no impone nombre al nivel

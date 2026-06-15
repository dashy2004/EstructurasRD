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


def test_columna_y_muro_continuos():
    from motor_fea.edificio.modelo import Columna, Muro, Zapata

    col = Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=6.0, material="H210",
                  zapata=Zapata(ancho=1.2, largo=1.2, peralte=0.4))
    assert (col.cota_base, col.cota_tope) == (0.0, 6.0)   # rango vertical continuo
    assert col.zapata.ancho == 1.2

    muro = Muro(id=2, linea=((0, 0), (0, 5)), espesor=0.20,
                cota_base=0.0, cota_tope=6.0, material="H210")
    assert muro.zapata is None                            # zapata opcional


def test_proyecto_contenedores_y_orden_de_niveles():
    from motor_fea.edificio.modelo import (
        CargasGlobales, Edificio, Metadata, Nivel, Proyecto,
    )

    n1 = Nivel(id=1, nombre="N1", cota=0.0)
    n2 = Nivel(id=2, nombre="N2", cota=3.0)
    edi = Edificio(id=1, nombre="Bloque A", niveles=[n2, n1])   # desordenados a propósito
    proy = Proyecto(metadata=Metadata(nombre="Demo"),
                    cargas_globales=CargasGlobales(muerta_adicional=1.5, viva=2.0),
                    combinaciones=["1.2D+1.6L"], edificios=[edi])

    assert [n.cota for n in edi.niveles_ordenados()] == [0.0, 3.0]   # ordena por cota
    assert edi.cota_minima() == 0.0
    assert proy.metadata.nombre == "Demo"
    assert proy.combinaciones == ["1.2D+1.6L"]


def test_columna_continua_atraviesa_los_tres_niveles():
    from motor_fea.edificio.modelo import Columna, Edificio, Nivel

    niveles = [Nivel(id=1, nombre="N1", cota=0.0),
               Nivel(id=2, nombre="N2", cota=3.0),
               Nivel(id=3, nombre="N3", cota=6.0)]
    col = Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=6.0, material="H210")
    edi = Edificio(id=1, nombre="Bloque A", niveles=niveles,
                   elementos_verticales=[col])

    atravesados = edi.niveles_atravesados(col)
    assert [n.cota for n in atravesados] == [0.0, 3.0, 6.0]   # conectada a los 3

    parcial = Columna(id=2, posicion=(1, 1), base=0.3, peralte=0.3,
                      cota_base=3.0, cota_tope=6.0, material="H210")
    assert [n.cota for n in edi.niveles_atravesados(parcial)] == [3.0, 6.0]

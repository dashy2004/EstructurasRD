import math

import pytest

from motor_fea.core.membrana import constitutiva_plana


def test_constitutiva_plana_valores_cerrados():
    E, nu = 2.0e10, 0.2
    D = constitutiva_plana(E, nu)
    factor = E / (1.0 - nu * nu)
    assert D[0][0] == pytest.approx(factor)
    assert D[1][1] == pytest.approx(factor)
    assert D[0][1] == pytest.approx(factor * nu)
    assert D[1][0] == pytest.approx(factor * nu)
    assert D[2][2] == pytest.approx(factor * (1.0 - nu) / 2.0)
    assert D[0][2] == 0.0 and D[1][2] == 0.0
    assert D[2][0] == 0.0 and D[2][1] == 0.0

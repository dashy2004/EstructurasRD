"""CLI del motor — frontera de integración con el C# (ver ADR-0001).

B0: sólo expone ``--version`` y un ``--check`` que valida un modelo trivial,
para fijar el contrato del ejecutable. El análisis real (leer un modelo JSON,
resolver y emitir resultados JSON) aterriza en B1+.
"""
from __future__ import annotations

import argparse
import sys

from motor_fea import __version__


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="motor-fea", description="Motor FEA nativo de EstructurasRD.")
    parser.add_argument("--version", action="store_true", help="Imprime la versión y sale.")
    args = parser.parse_args(argv)

    if args.version:
        print(__version__)
        return 0

    parser.print_help()
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())

"""CLI del motor — frontera de integración con el C# (ver ADR-0001).

Contrato (paridad con el patrón de ``Losas.exe``): el C# pasa un modelo
estructural en JSON y recibe resultados en JSON. Uso::

    motor-fea --version
    motor-fea --analyze modelo.json            # resultados JSON por stdout
    cat modelo.json | motor-fea --analyze -     # '-' = leer de stdin

El esquema JSON está documentado en :mod:`motor_fea.api.contrato`.
"""
from __future__ import annotations

import argparse
import sys

from motor_fea import __version__
from motor_fea.api.contrato import analizar_json


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="motor-fea", description="Motor FEA nativo de EstructurasRD.")
    parser.add_argument("--version", action="store_true", help="Imprime la versión y sale.")
    parser.add_argument("--analyze", metavar="MODELO.json",
                        help="Resuelve el modelo JSON (ruta o '-' para stdin) y emite resultados JSON.")
    args = parser.parse_args(argv)

    if args.version:
        print(__version__)
        return 0

    if args.analyze:
        try:
            texto = sys.stdin.read() if args.analyze == "-" else _leer(args.analyze)
            print(analizar_json(texto))
            return 0
        except (OSError, ValueError) as ex:
            print(f"error: {ex}", file=sys.stderr)
            return 1

    parser.print_help()
    return 0


def _leer(ruta: str) -> str:
    with open(ruta, encoding="utf-8") as f:
        return f.read()


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())

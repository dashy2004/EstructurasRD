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
from motor_fea.api.contrato import analizar_json, disenar_losa_json


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="motor-fea", description="Motor FEA nativo de EstructurasRD.")
    parser.add_argument("--version", action="store_true", help="Imprime la versión y sale.")
    parser.add_argument("--analyze", metavar="MODELO.json",
                        help="Resuelve el modelo JSON (ruta o '-' para stdin) y emite resultados JSON.")
    parser.add_argument("--disenar-losa", metavar="PARAMS.json", dest="disenar_losa",
                        help="Diseña una losa por FEM desde un JSON de parámetros (ruta o '-') y emite JSON.")
    parser.add_argument("--serve", nargs="?", const="", metavar="MODELO.json",
                        help="Levanta el visor 3D WebXR (requiere el extra api). "
                             "Sin MODELO.json sirve un pórtico de ejemplo.")
    parser.add_argument("--host", default="127.0.0.1", help="Host del servidor (--serve).")
    parser.add_argument("--port", type=int, default=8000, help="Puerto del servidor (--serve).")
    args = parser.parse_args(argv)

    if args.version:
        print(__version__)
        return 0

    if args.analyze:
        return _ejecutar(args.analyze, analizar_json)

    if args.disenar_losa:
        return _ejecutar(args.disenar_losa, disenar_losa_json)

    if args.serve is not None:
        try:
            from motor_fea.api.servidor import servir
        except ImportError:
            print("error: el visor requiere FastAPI. Instala: pip install -e '.[api]'",
                  file=sys.stderr)
            return 1
        servir(args.serve or None, args.host, args.port)
        return 0

    parser.print_help()
    return 0


def _ejecutar(ruta: str, pipeline) -> int:
    """Lee JSON (ruta o '-' = stdin), aplica el pipeline y lo imprime; 1 en error."""
    try:
        texto = sys.stdin.read() if ruta == "-" else _leer(ruta)
        print(pipeline(texto))
        return 0
    except (OSError, ValueError, KeyError) as ex:
        print(f"error: {ex}", file=sys.stderr)
        return 1


def _leer(ruta: str) -> str:
    with open(ruta, encoding="utf-8") as f:
        return f.read()


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())

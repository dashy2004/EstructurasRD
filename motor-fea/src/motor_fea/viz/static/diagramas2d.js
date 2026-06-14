// Panel 2D de diagramas P/V/M: función pura que arma un SVG con los 6
// mini-diagramas (N, Vy, Vz, T, My, Mz) apilados de un elemento del DTO
// `esfuerzos`. No conoce three.js; no muta el DOM global (solo crea nodos).
import { nodo } from './svgutil.js';

// k = índice de la columna del valor en una fila del diagrama [s, N, Vy, Vz, T, My, Mz].
// momento → unidad kN·m; fuerza → kN. Ambos se dividen por 1000.
const COMP = [
  { k: 1, nombre: 'N',  unidad: 'kN',   momento: false },
  { k: 2, nombre: 'Vy', unidad: 'kN',   momento: false },
  { k: 3, nombre: 'Vz', unidad: 'kN',   momento: false },
  { k: 4, nombre: 'T',  unidad: 'kN·m', momento: true  },
  { k: 5, nombre: 'My', unidad: 'kN·m', momento: true  },
  { k: 6, nombre: 'Mz', unidad: 'kN·m', momento: true  },
];

// elemento: { longitud, diagrama: [[s,N,Vy,Vz,T,My,Mz], ...] }
export function diagramaSVG(elemento, opts = {}) {
  const W = opts.ancho || 230;
  const H = opts.alto || 40;        // alto del área de trazado por componente
  const gap = 22;                   // espacio para la etiqueta encima
  const pad = 6;
  const colPos = opts.colorPos || '#ff4444';
  const colNeg = opts.colorNeg || '#4488ff';
  const filas = elemento.diagrama || [];
  const Ln = elemento.longitud || 1;
  const total = COMP.length * (H + gap);

  const svg = nodo('svg', {
    width: W, height: total, viewBox: `0 0 ${W} ${total}`,
  });

  COMP.forEach((c, ci) => {
    const y0 = ci * (H + gap) + gap;   // borde superior del trazado
    const mid = y0 + H / 2;            // línea base (cero)

    let m = 0;
    for (const f of filas) m = Math.max(m, Math.abs(f[c.k]));
    const esc = m > 0 ? (H / 2 - 2) / m : 0;   // auto-escala; evita /0

    const pico = (m / 1000).toFixed(c.momento ? 1 : 0);
    const etq = nodo('text', { x: pad, y: y0 - 6, fill: '#fff', 'font-size': 11, 'font-family': 'sans-serif' });
    etq.textContent = `${c.nombre}  |máx| = ${pico} ${c.unidad}`;
    svg.appendChild(etq);

    svg.appendChild(nodo('line', {
      x1: pad, y1: mid, x2: W - pad, y2: mid, stroke: '#888', 'stroke-width': 1,
    }));

    if (!filas.length) return;
    const X = (s) => pad + (s / Ln) * (W - 2 * pad);
    const Y = (v) => mid - v * esc;

    // Relleno por signo: un trapecio por tramo, coloreado por el signo del
    // valor medio del tramo (diagramas lineales por tramos → exacto).
    for (let i = 0; i < filas.length - 1; i++) {
      const f0 = filas[i], f1 = filas[i + 1];
      const v0 = f0[c.k], v1 = f1[c.k];
      const fill = (v0 + v1) >= 0 ? colPos : colNeg;
      svg.appendChild(nodo('polygon', {
        points: `${X(f0[0])},${mid} ${X(f0[0])},${Y(v0)} ${X(f1[0])},${Y(v1)} ${X(f1[0])},${mid}`,
        fill, 'fill-opacity': 0.35, stroke: 'none',
      }));
    }

    svg.appendChild(nodo('polyline', {
      points: filas.map((f) => `${X(f[0])},${Y(f[c.k])}`).join(' '),
      fill: 'none', stroke: '#fff', 'stroke-width': 1.5,
    }));
  });

  return svg;
}

// Corte global: funciones puras para rebanar el modelo con un plano eje-alineado y
// dibujar el esquema 2D de los miembros cortados. No conocen three.js ni el DOM global
// (sólo crean nodos SVG vía nodo()). Z es el eje vertical del modelo.
import { nodo } from './svgutil.js';

// Orientaciones del plano: eje normal k (0=X,1=Y,2=Z) y los dos ejes proyectados
// (u = horizontal del dibujo, v = vertical del dibujo). Las elevaciones llevan Z (vertical) en v.
//   planta (z):     normal Z → (u,v) = (X, Y)   — vista en planta
//   elevación (x):  normal X → (u,v) = (Y, Z)   — Z arriba
//   elevación (y):  normal Y → (u,v) = (X, Z)   — Z arriba
export const ORIENTACIONES = {
  planta: { k: 2, u: 0, v: 1, etq: 'planta (z)' },
  elev_x: { k: 0, u: 1, v: 2, etq: 'elevación (x)' },
  elev_y: { k: 1, u: 0, v: 2, etq: 'elevación (y)' },
};

const EPS = 1e-9;
const COLOR_TIPO = { columna: '#4a90d9', viga: '#d98a4a' };   // mismo palette que el 3D

// segmentos = [{ id, pi:[x,y,z], pj:[x,y,z], longitud, b, h, tipo }]
// Devuelve cruces = [{ id, u, v, P:[x,y,z], s, b, h, tipo }] para los que cruzan el plano k=c.
export function intersectarPlano(segmentos, orient, c) {
  const { k, u, v } = orient;
  const cruces = [];
  for (const seg of segmentos) {
    const den = seg.pj[k] - seg.pi[k];
    if (Math.abs(den) < EPS) continue;            // paralelo/contenido en el plano: se omite
    const f = (c - seg.pi[k]) / den;
    if (f < 0 || f > 1) continue;                 // no cruza el segmento
    const P = [
      seg.pi[0] + (seg.pj[0] - seg.pi[0]) * f,
      seg.pi[1] + (seg.pj[1] - seg.pi[1]) * f,
      seg.pi[2] + (seg.pj[2] - seg.pi[2]) * f,
    ];
    cruces.push({
      id: seg.id, u: P[u], v: P[v], P,
      s: f * (seg.longitud || 0),
      b: seg.b, h: seg.h, tipo: seg.tipo,
    });
  }
  return cruces;
}

// Color divergente (blanco→azul para v<0, blanco→rojo para v>0). Sólo se usa si opts.comp.
function colorDivergente(val, maxAbs) {
  const s = Math.max(-1, Math.min(1, val / (maxAbs || 1)));
  const m = Math.abs(s);
  const r = s > 0 ? 255 : Math.round(255 * (1 - m));
  const g = Math.round(255 * (1 - m));
  const b = s < 0 ? 255 : Math.round(255 * (1 - m));
  return `rgb(${r},${g},${b})`;
}

// cruces = salida de intersectarPlano (opcionalmente con .fuerzas adjuntas).
// opts = { orientEtq, c, ancho, alto, comp, maxAbs }. Devuelve un SVGElement.
// Cada miembro cortado es un rect b×h con data-id (para el pick del esquema).
export function corteSVG(cruces, opts = {}) {
  const W = opts.ancho || 260;
  const H = opts.alto || 220;
  const pad = 18;
  const total = H + 28;
  const cc = (opts.c != null ? opts.c : 0);
  const svg = nodo('svg', { width: W, height: total, viewBox: `0 0 ${W} ${total}` });

  const etiqueta = (t) => {
    const e = nodo('text', { x: pad, y: H + 18, fill: '#fff', 'font-size': 11, 'font-family': 'sans-serif' });
    e.textContent = t; svg.appendChild(e);
  };

  if (!cruces.length) {
    const e = nodo('text', { x: W / 2, y: H / 2, fill: '#888', 'font-size': 13,
      'font-family': 'sans-serif', 'text-anchor': 'middle' });
    e.textContent = '0 cortes';
    svg.appendChild(e);
    etiqueta(`${opts.orientEtq || ''}  @ ${cc.toFixed(2)} m · 0 cortes`);
    return svg;
  }

  // Auto-fit: rango de (u,v) incluyendo el tamaño de cada rect, con margen.
  let uMin = Infinity, uMax = -Infinity, vMin = Infinity, vMax = -Infinity;
  for (const cr of cruces) {
    const hb = Math.max(cr.b, cr.h) / 2;
    uMin = Math.min(uMin, cr.u - hb); uMax = Math.max(uMax, cr.u + hb);
    vMin = Math.min(vMin, cr.v - hb); vMax = Math.max(vMax, cr.v + hb);
  }
  const du = (uMax - uMin) || 1, dv = (vMax - vMin) || 1;
  const dispW = W - 2 * pad, dispH = H - 2 * pad;
  const esc = Math.min(dispW / du, dispH / dv);
  const offU = pad + (dispW - du * esc) / 2;
  const offV = pad + (dispH - dv * esc) / 2;
  const X = (uu) => offU + (uu - uMin) * esc;
  const Y = (vv) => offV + (vMax - vv) * esc;   // v hacia arriba (SVG y invertido)

  for (const cr of cruces) {
    const w = Math.max(2, cr.b * esc);
    const h = Math.max(2, cr.h * esc);
    let fill = COLOR_TIPO[cr.tipo] || '#888';
    if (opts.comp != null && Array.isArray(cr.fuerzas) && opts.maxAbs > 0) {
      fill = colorDivergente(cr.fuerzas[opts.comp], opts.maxAbs);
    }
    svg.appendChild(nodo('rect', {
      x: X(cr.u) - w / 2, y: Y(cr.v) - h / 2, width: w, height: h,
      fill, stroke: '#fff', 'stroke-width': 0.75, 'data-id': cr.id }));
  }
  etiqueta(`${opts.orientEtq || ''}  @ ${cc.toFixed(2)} m · ${cruces.length} cortes`);
  return svg;
}

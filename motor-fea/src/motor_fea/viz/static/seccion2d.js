// Panel 2D de la sección transversal de un miembro: función pura que arma un SVG
// con el corte b×h, el armado (barras + estribo) si viene, un cue ligero de momento
// (cara en tracción) y un bloque con los 6 esfuerzos en la estación s. No conoce three.js.
import { nodo } from './svgutil.js';

const COMP = ['N', 'Vy', 'Vz', 'T', 'My', 'Mz'];
const ES_MOMENTO = [false, false, false, true, true, true];   // T, My, Mz → kN·m

// datos = { b, h, long, estribo, fuerzas:[N,Vy,Vz,T,My,Mz], designacion, cumple, s, L }
export function seccionSVG(datos, opts = {}) {
  const W = opts.ancho || 240;
  const Hsec = opts.altoSeccion || 200;     // alto del área de dibujo de la sección
  const pad = 14;
  const colBar = opts.colorBarra || '#c0392b';
  const colTracc = opts.colorTraccion || '#e67e22';
  const {
    b, h, long, estribo, fuerzas = [0, 0, 0, 0, 0, 0],
    designacion, cumple, s = 0, L = 1,
  } = datos;

  const dispW = W - 2 * pad, dispH = Hsec - 2 * pad;
  const esc = Math.min(dispW / (b || 1), dispH / (h || 1));   // m → px, llena el área
  const cx = W / 2, cy = pad + dispH / 2;
  const X = (x) => cx + x * esc;          // sección local (m, origen al centro) → SVG
  const Y = (y) => cy - y * esc;          // y arriba = +

  const altoTexto = 16 * (COMP.length + 1) + 12;
  const total = Hsec + altoTexto;
  const svg = nodo('svg', { width: W, height: total, viewBox: `0 0 ${W} ${total}` });

  // --- rectángulo b×h ---
  svg.appendChild(nodo('rect', { x: X(-b / 2), y: Y(h / 2), width: b * esc, height: h * esc,
    fill: 'none', stroke: '#aaa', 'stroke-width': 1.5 }));

  // --- estribo ---
  if (estribo && estribo.w > 0 && estribo.h > 0) {
    svg.appendChild(nodo('rect', {
      x: X(-estribo.w / 2), y: Y(estribo.h / 2),
      width: estribo.w * esc, height: estribo.h * esc,
      fill: 'none', stroke: '#2ecc71', 'stroke-width': 1 }));
  }

  // --- barras longitudinales ---
  if (Array.isArray(long)) {
    for (const bar of long) {
      svg.appendChild(nodo('circle', {
        cx: X(bar.x), cy: Y(bar.y), r: Math.max(1.5, (bar.d / 2) * esc), fill: colBar }));
    }
  }

  // --- cue de cara en tracción, encima de todo (Mz → caras verticales; My → horizontales) ---
  const My = fuerzas[4], Mz = fuerzas[5];
  if (Math.abs(Mz) > 1e-6) {
    const xc = Mz > 0 ? X(b / 2) : X(-b / 2);
    svg.appendChild(nodo('line', { x1: xc, y1: Y(h / 2), x2: xc, y2: Y(-h / 2),
      stroke: colTracc, 'stroke-width': 4 }));
  }
  if (Math.abs(My) > 1e-6) {
    const yc = My > 0 ? Y(h / 2) : Y(-h / 2);
    svg.appendChild(nodo('line', { x1: X(-b / 2), y1: yc, x2: X(b / 2), y2: yc,
      stroke: colTracc, 'stroke-width': 4 }));
  }

  // --- bloque de texto ---
  let ty = Hsec + 12;
  const linea = (t) => {
    const e = nodo('text', { x: pad, y: ty, fill: '#fff', 'font-size': 11, 'font-family': 'sans-serif' });
    e.textContent = t; svg.appendChild(e); ty += 16;
  };
  const enc = designacion
    ? `${designacion}${cumple === undefined ? '' : (cumple ? ' · cumple' : ' · NO cumple')}`
    : 'sección';
  linea(`${enc}   s = ${s.toFixed(2)} m  (s/L = ${(L ? s / L : 0).toFixed(2)})`);
  COMP.forEach((nombre, k) => {
    const v = fuerzas[k] / 1000;
    const u = ES_MOMENTO[k] ? 'kN·m' : 'kN';
    let etq = `${nombre} = ${v.toFixed(ES_MOMENTO[k] ? 1 : 0)} ${u}`;
    if (nombre === 'N') etq += v >= 0 ? '  (tracc)' : '  (compr)';
    linea(etq);
  });

  return svg;
}

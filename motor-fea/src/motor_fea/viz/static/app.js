import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';
import { crearShell } from './shell.js';
import { diagramaSVG } from './diagramas2d.js';
import { seccionSVG } from './seccion2d.js';
import { descargarSVG, descargarPNG } from './svgutil.js';

const msg = document.getElementById('msg');
const setMsg = (t) => { msg.textContent = t; };

// --- Escena base ---
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x101418);
scene.add(new THREE.GridHelper(50, 50, 0x444444, 0x222222));
scene.add(new THREE.AxesHelper(2));
scene.add(new THREE.HemisphereLight(0xffffff, 0x303030, 1.3));

const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.05, 1000);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.setPixelRatio(devicePixelRatio);
renderer.xr.enabled = true;
document.body.appendChild(renderer.domElement);

const rig = new THREE.Group();
rig.add(camera);
scene.add(rig);

const controls = new OrbitControls(camera, renderer.domElement);

const MAT = {
  columna: new THREE.MeshStandardMaterial({ color: 0x4a90d9 }),
  viga:    new THREE.MeshStandardMaterial({ color: 0xd98a4a }),
};
const MAT_LONG = new THREE.MeshStandardMaterial({ color: 0xc0392b });   // armado de ejemplo
const MAT_EST = new THREE.LineBasicMaterial({ color: 0x2ecc71 });       // estribo
const MAT_OK = new THREE.MeshStandardMaterial({ color: 0x9aa0a6 });     // diseño: cumple (acero gris)
const MAT_FALLA = new THREE.MeshStandardMaterial({ color: 0xff3b30 });  // diseño: NO cumple (rojo)

// --- Estado ---
const basePos = {};
const barras = [];           // { mesh, i, j, id }
let resultados = null;
let esfuerzos = null;        // DTO de esfuerzos por elemento (para el pick readout)
let cintasGroup = null;      // overlay de cintas 3D (Group de Meshes)
let diagActivo = false;
let diagComp = 0;            // componente activo de la cinta: N=0 … Mz=5
let frameBbox = null;

let anilloSeccion = null;    // marcador 3D del plano de corte
let secActivo = false;
let secElId = null;          // id del miembro seleccionado
let secSvgActual = null;     // último SVGElement dibujado (para export)

let losa = null;
let losaMesh = null;
let losaActiva = false;
let campoLosa = 'deflexion';

let armado = null;
let armadoGroup = null;
let refuerzoActivo = false;

let diseno = null;
let disenoGroup = null;
let disenoActivo = false;

let estado = 'sin-deformar';
let exag = 0;
let playing = true;
let tAcum = 0;
let lastT = null;
const T_DISPLAY = 2.0;

const selEstado = document.getElementById('estado');
const exagInput = document.getElementById('exag');
const btnPlay = document.getElementById('play');
const info = document.getElementById('info');
const inpFc = document.getElementById('fc');
const inpFy = document.getElementById('fy');
const inpRec = document.getElementById('rec');
const btnRedi = document.getElementById('redisenar');
const selDiagComp = document.getElementById('diag-comp');
const diagSvg = document.getElementById('diag-svg');
const secDiv = document.getElementById('sec');
const secHost = document.getElementById('sec-svg');
const secSlider = document.getElementById('sec-s');
const btnSecSvg = document.getElementById('sec-svg-dl');
const btnSecPng = document.getElementById('sec-png-dl');

// --- Barras ---
function addBarra(b) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, 1), MAT[b.tipo] || MAT.viga);
  scene.add(mesh);
  barras.push({ mesh, i: b.i, j: b.j, id: b.id, b: b.b, h: b.h });
}

function despNodo(id, fase) {
  if (!resultados) return new THREE.Vector3();
  if (estado === 'deformada') {
    const d = resultados.deformada.desplazamientos[id];
    if (d) return new THREE.Vector3(d[0], d[1], d[2]).multiplyScalar(exag);
  } else if (estado.startsWith('modo-')) {
    const m = resultados.modos[parseInt(estado.slice(5), 10) - 1];
    if (m) {
      const f = m.forma[id];
      if (f) return new THREE.Vector3(f[0], f[1], f[2]).multiplyScalar(exag * fase);
    }
  }
  return new THREE.Vector3();
}

function posDef(id, fase) {
  const base = basePos[id];
  if (!base) return null;
  return base.clone().add(despNodo(id, fase));
}

function actualizarBarras(fase) {
  for (const bar of barras) {
    const vi = posDef(bar.i, fase);
    const vj = posDef(bar.j, fase);
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    bar.mesh.position.copy(vi).lerp(vj, 0.5);
    bar.mesh.lookAt(vj);
    bar.mesh.scale.z = L === 0 ? 1e-6 : L;
  }
}

function encuadrar(min, max) {
  const mn = new THREE.Vector3(min[0], min[1], min[2]);
  const mx = new THREE.Vector3(max[0], max[1], max[2]);
  const centro = mn.clone().add(mx).multiplyScalar(0.5);
  const radio = Math.max(mn.distanceTo(mx) / 2, 1);
  controls.target.copy(centro);
  camera.position.copy(centro).add(new THREE.Vector3(radio * 1.6, radio * 1.2, radio * 1.6));
  controls.update();
}

// --- Losa ---
function valorLosa(campoNombre, i, j) {
  return losa.campos[campoNombre].valores[`${i},${j}`];
}

function colorDeCampo(nombre, v, min, max) {
  if (nombre === 'deflexion') {
    const t = max > min ? (v - min) / (max - min) : 0;
    return new THREE.Color().setHSL((1 - t) * 240 / 360, 1, 0.5);
  }
  const M = Math.max(Math.abs(min), Math.abs(max)) || 1;
  const s = v / M;
  const destino = s < 0 ? new THREE.Color(0x2222ff) : new THREE.Color(0xff2222);
  return new THREE.Color(1, 1, 1).lerp(destino, Math.min(1, Math.abs(s)));
}

function construirLosa() {
  const { a, b, nx, ny } = losa;
  const nvx = (nx + 1) * (ny + 1);
  const pos = new Float32Array(nvx * 3);
  const col = new Float32Array(nvx * 3);
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      pos[n * 3] = i * a / nx;
      pos[n * 3 + 1] = j * b / ny;
      pos[n * 3 + 2] = 0;
    }
  }
  const idx = [];
  for (let cj = 0; cj < ny; cj++) {
    for (let ci = 0; ci < nx; ci++) {
      const n00 = cj * (nx + 1) + ci, n10 = cj * (nx + 1) + ci + 1;
      const n11 = (cj + 1) * (nx + 1) + ci + 1, n01 = (cj + 1) * (nx + 1) + ci;
      idx.push(n00, n10, n11, n00, n11, n01);
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  geo.setIndex(idx);
  const mat = new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide });
  losaMesh = new THREE.Mesh(geo, mat);
  losaMesh.visible = false;
  scene.add(losaMesh);
}

function colorearLosa(nombre) {
  const campo = losa.campos[nombre];
  const col = losaMesh.geometry.getAttribute('color');
  const { nx, ny } = losa;
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      const c = colorDeCampo(nombre, valorLosa(nombre, i, j), campo.min, campo.max);
      col.setXYZ(n, c.r, c.g, c.b);
    }
  }
  col.needsUpdate = true;
}

function actualizarLosa() {
  const pos = losaMesh.geometry.getAttribute('position');
  const { nx, ny } = losa;
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      const w_m = valorLosa('deflexion', i, j) / 1000;
      pos.setZ(n, -w_m * exag);
    }
  }
  pos.needsUpdate = true;
}

// --- Jaula de armado (ejemplo o diseñada) ---
// Group de Groups por elemento, sin scale.z (los cilindros llevan su largo L).
// matLongFn(el) elige el material de las barras (ej.: por cumple en el diseño).
function construirJaula(dto, matLongFn) {
  const grupo = new THREE.Group();
  grupo.visible = false;
  for (const el of dto.elementos) {
    const vi = basePos[el.i], vj = basePos[el.j];
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    if (L === 0) continue;
    const g = new THREE.Group();
    g.position.copy(vi).lerp(vj, 0.5);
    g.lookAt(vj);
    const matLong = matLongFn(el);
    for (const bar of el.long) {
      const geo = new THREE.CylinderGeometry(bar.d / 2, bar.d / 2, L, 8);
      geo.rotateX(Math.PI / 2);
      const cil = new THREE.Mesh(geo, matLong);
      cil.position.set(bar.x, bar.y, 0);
      g.add(cil);
    }
    const { w, h, s } = el.estribo;
    const pts = [
      new THREE.Vector3(-w / 2, -h / 2, 0), new THREE.Vector3(w / 2, -h / 2, 0),
      new THREE.Vector3(w / 2, h / 2, 0), new THREE.Vector3(-w / 2, h / 2, 0),
    ];
    const loopGeo = new THREE.BufferGeometry().setFromPoints(pts);
    const nTramos = Math.max(2, Math.floor(L / s));
    for (let k = 0; k <= nTramos; k++) {
      const loop = new THREE.LineLoop(loopGeo, MAT_EST);
      loop.position.z = -L / 2 + k * (L / nTramos);
      g.add(loop);
    }
    grupo.add(g);
  }
  scene.add(grupo);
  return grupo;
}

// --- Cintas 3D de diagramas (overlay) ---
// Para el componente c (0=N … 5=Mz), una tira de triángulos por barra entre la
// polilínea base (eje del miembro) y la polilínea desplazada (valor × escala).
// Dirección de despliegue derivada del eje + up global (aprox. de orientación,
// ver spec §6.3): Mz,Vy,N,T → t1 ; Vz,My → t2.
function construirCintas(c) {
  const grupo = new THREE.Group();
  grupo.visible = false;
  if (!esfuerzos) { scene.add(grupo); return grupo; }

  let maxAbs = 0;
  for (const el of esfuerzos.elementos)
    for (const fila of el.diagrama) maxAbs = Math.max(maxAbs, Math.abs(fila[c + 1]));
  const norm = maxAbs > 0 ? exag / maxAbs : 0;   // valor pico → offset = exag (m)

  const up = new THREE.Vector3(0, 1, 0);
  const altUp = new THREE.Vector3(1, 0, 0);

  for (const el of esfuerzos.elementos) {
    const bar = barras.find((b) => b.id === el.id);
    if (!bar) continue;
    const vi = basePos[bar.i], vj = basePos[bar.j];
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    if (L === 0) continue;                         // largo 0: se omite
    if (el.longitud <= 0 || el.diagrama.length < 2) continue;   // diagrama degenerado

    const axis = vj.clone().sub(vi).normalize();
    let t1 = new THREE.Vector3().crossVectors(axis, up);
    if (t1.lengthSq() < 1e-6) t1 = new THREE.Vector3().crossVectors(axis, altUp);
    t1.normalize();
    const t2 = new THREE.Vector3().crossVectors(axis, t1).normalize();
    const dir = (c === 2 || c === 4) ? t2 : t1;    // Vz, My → t2 ; resto → t1

    const filas = el.diagrama;
    const n = filas.length;
    const pos = new Float32Array(n * 2 * 3);
    const col = new Float32Array(n * 2 * 3);
    for (let k = 0; k < n; k++) {
      const s = filas[k][0];
      const val = filas[k][c + 1];
      const base = vi.clone().lerp(vj, el.longitud ? s / el.longitud : 0);
      const off = base.clone().add(dir.clone().multiplyScalar(val * norm));
      pos[k * 6 + 0] = base.x; pos[k * 6 + 1] = base.y; pos[k * 6 + 2] = base.z;
      pos[k * 6 + 3] = off.x;  pos[k * 6 + 4] = off.y;  pos[k * 6 + 5] = off.z;
      const cc = colorDeCampo('diagrama', val, -maxAbs, maxAbs);   // divergente por signo
      col[k * 6 + 0] = cc.r; col[k * 6 + 1] = cc.g; col[k * 6 + 2] = cc.b;
      col[k * 6 + 3] = cc.r; col[k * 6 + 4] = cc.g; col[k * 6 + 5] = cc.b;
    }
    const idx = [];
    for (let k = 0; k < n - 1; k++) {
      const b0 = k * 2, o0 = k * 2 + 1, b1 = (k + 1) * 2, o1 = (k + 1) * 2 + 1;
      idx.push(b0, o0, o1, b0, o1, b1);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    geo.setIndex(idx);
    const mat = new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide });
    grupo.add(new THREE.Mesh(geo, mat));
  }
  scene.add(grupo);
  return grupo;
}

function disposeCintas() {
  if (!cintasGroup) return;
  scene.remove(cintasGroup);
  cintasGroup.traverse((o) => {
    if (o.geometry) o.geometry.dispose();
    if (o.material) o.material.dispose();
  });
  cintasGroup = null;
}

function reconstruirCintas() {
  disposeCintas();
  cintasGroup = construirCintas(diagComp);
  cintasGroup.visible = true;
}

function etiquetaCintas() {
  const nombres = esfuerzos ? esfuerzos.orden_componentes : ['N', 'Vy', 'Vz', 'T', 'My', 'Mz'];
  info.textContent = `diagramas 3D — ${nombres[diagComp]}`;
}

function fantasma(on) {
  for (const m of [MAT.columna, MAT.viga]) {
    m.transparent = on;
    m.opacity = on ? 0.25 : 1.0;
    m.depthWrite = !on;
  }
}

// --- Panel ---
function fsDe(est) {
  if (!resultados) return 1;
  if (est === 'deformada') return resultados.deformada.factor_sugerido;
  if (est.startsWith('modo-')) {
    const m = resultados.modos[parseInt(est.slice(5), 10) - 1];
    return m ? m.factor_sugerido : 1;
  }
  return 1;
}

function resetOverlays() {
  losaActiva = false;
  refuerzoActivo = false;
  disenoActivo = false;
  diagActivo = false;
  secActivo = false;
  if (losaMesh) losaMesh.visible = false;
  if (armadoGroup) armadoGroup.visible = false;
  if (disenoGroup) disenoGroup.visible = false;
  if (cintasGroup) cintasGroup.visible = false;
  disposeAnillo();
  if (secDiv) secDiv.style.display = 'none';
  fantasma(false);
  for (const bar of barras) bar.mesh.visible = true;
}

function entrarLosa(est) {
  campoLosa = est.slice(5);
  losaActiva = true;
  for (const bar of barras) bar.mesh.visible = false;
  losaMesh.visible = true;
  colorearLosa(campoLosa);
  const fs = losa.factor_sugerido;
  exagInput.min = 0; exagInput.max = fs * 5; exagInput.step = fs / 100;
  exagInput.value = fs; exag = fs;
  const campo = losa.campos[campoLosa];
  const et = { deflexion: 'deflexión', momento_mx: 'momento Mx', momento_my: 'momento My' }[campoLosa];
  info.textContent = `${et}: ${campo.min.toFixed(1)} … ${campo.max.toFixed(1)} ${campo.unidad}`;
  encuadrar([0, 0, 0], [losa.a, losa.b, 0]);
}

function entrarRefuerzo() {
  refuerzoActivo = true;
  fantasma(true);
  if (armadoGroup) armadoGroup.visible = true;
  exagInput.min = 0; exagInput.max = 1; exagInput.step = 1;
  exagInput.value = 0; exag = 0;
  info.textContent = armado
    ? `armado de ejemplo (ρ≈1% col · As_mín viga) — ${armado.elementos.length} elementos`
    : '';
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function entrarDiseno() {
  disenoActivo = true;
  fantasma(true);
  if (disenoGroup) disenoGroup.visible = true;
  exagInput.min = 0; exagInput.max = 1; exagInput.step = 1;
  exagInput.value = 0; exag = 0;
  const n = diseno.elementos.length;
  const ok = diseno.elementos.filter((el) => el.cumple).length;
  info.textContent = `diseño LRFD — ${ok}/${n} cumplen`;
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function entrarDiagramas() {
  diagActivo = true;
  // las barras quedan en su posición base (estado 'diagramas' → despNodo = 0);
  // la escala exag controla la altura de las cintas.
  const span = frameBbox
    ? new THREE.Vector3(...frameBbox.max).distanceTo(new THREE.Vector3(...frameBbox.min))
    : 10;
  exagInput.min = 0; exagInput.max = span; exagInput.step = span / 100;
  exagInput.value = span * 0.25; exag = span * 0.25;
  reconstruirCintas();
  etiquetaCintas();
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function entrarSeccion() {
  secActivo = true;
  secElId = null;
  secSvgActual = null;
  secHost.replaceChildren();
  if (diagSvg) diagSvg.replaceChildren();   // evita el diagrama viejo encima del panel
  secDiv.style.display = 'flex';
  info.textContent = 'toca una barra para ver su sección';
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function setEstado(nuevo) {
  const veniaEspecial = losaActiva || refuerzoActivo || disenoActivo || diagActivo || secActivo;
  estado = nuevo;
  resetOverlays();
  if (nuevo.startsWith('losa-')) { entrarLosa(nuevo); return; }
  if (nuevo === 'refuerzo') { entrarRefuerzo(); return; }
  if (nuevo === 'diseno') { entrarDiseno(); return; }
  if (nuevo === 'diagramas') { entrarDiagramas(); return; }
  if (nuevo === 'seccion') { entrarSeccion(); return; }
  if (veniaEspecial && frameBbox) encuadrar(frameBbox.min, frameBbox.max);
  const fs = fsDe(estado);
  exagInput.min = 0; exagInput.max = fs * 5; exagInput.step = fs / 100;
  exagInput.value = estado === 'sin-deformar' ? 0 : fs;
  exag = parseFloat(exagInput.value);
  if (estado.startsWith('modo-')) {
    const m = resultados.modos[parseInt(estado.slice(5), 10) - 1];
    info.textContent = m ? `T = ${m.periodo.toFixed(2)} s` : '';
  } else if (estado === 'deformada') {
    info.textContent = 'estático';
  } else {
    info.textContent = '';
  }
}

selEstado.addEventListener('change', () => setEstado(selEstado.value));
exagInput.addEventListener('input', () => {
  exag = parseFloat(exagInput.value);
  if (diagActivo) reconstruirCintas();
});
selDiagComp.addEventListener('change', () => {
  diagComp = parseInt(selDiagComp.value, 10);
  if (diagActivo) { reconstruirCintas(); etiquetaCintas(); }
});
secSlider.addEventListener('input', () => { if (secActivo) dibujarSeccion(); });
btnSecSvg.addEventListener('click', () => descargarSVG(secSvgActual, 'seccion.svg'));
btnSecPng.addEventListener('click', () => descargarPNG(secSvgActual, 'seccion.png'));
btnPlay.addEventListener('click', () => {
  playing = !playing;
  btnPlay.textContent = playing ? '⏸' : '▶';
  btnPlay.setAttribute('aria-label', playing ? 'pausar' : 'reanudar');
});

// --- Picking: tocar la losa (valor) o un elemento en diseño (etiqueta) ---
const punteroRay = new THREE.Raycaster();
const ndc = new THREE.Vector2();
renderer.domElement.addEventListener('pointerdown', (ev) => {
  if (renderer.xr.isPresenting) return;
  ndc.x = (ev.clientX / innerWidth) * 2 - 1;
  ndc.y = -(ev.clientY / innerHeight) * 2 + 1;
  punteroRay.setFromCamera(ndc, camera);
  if (losaActiva && losaMesh) {
    const hits = punteroRay.intersectObject(losaMesh);
    if (hits.length) mostrarValorEnPunto(hits[0].point.x, hits[0].point.y);
  } else if (disenoActivo && diseno) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    const el = bar && diseno.elementos.find((e) => e.id === bar.id);
    if (el) mostrarDiseno(el);
  } else if (secActivo && esfuerzos) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    if (!bar) return;
    const el = esfuerzos.elementos.find((e) => e.id === bar.id);
    if (!el) return;
    secElId = bar.id;
    const L = el.longitud || 1;
    secSlider.min = 0; secSlider.max = L; secSlider.step = L / 100; secSlider.value = L / 2;
    construirAnilloSeccion(el);
    dibujarSeccion();
  } else if (esfuerzos && !refuerzoActivo && !diagActivo && !secActivo) {   // panel 2D solo en modos no-overlay (spec §5.2/§7)
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    if (!bar) return;
    const txt = resumenEsfuerzos(bar.id);
    if (txt) info.textContent = txt;
    dibujarDiagramas2D(bar.id);
  }
});

function mostrarValorEnPunto(x, y) {
  const { a, b, nx, ny } = losa;
  const lx = a / nx, ly = b / ny;
  const ci = Math.max(0, Math.min(nx - 1, Math.floor(x / lx)));
  const cj = Math.max(0, Math.min(ny - 1, Math.floor(y / ly)));
  const fx = Math.max(0, Math.min(1, (x - ci * lx) / lx));
  const fy = Math.max(0, Math.min(1, (y - cj * ly) / ly));
  const V = (i, j) => valorLosa(campoLosa, i, j);
  const v = (1 - fx) * (1 - fy) * V(ci, cj) + fx * (1 - fy) * V(ci + 1, cj)
          + fx * fy * V(ci + 1, cj + 1) + (1 - fx) * fy * V(ci, cj + 1);
  const et = { deflexion: 'w', momento_mx: 'Mx', momento_my: 'My' }[campoLosa];
  info.textContent = `${et} = ${v.toFixed(2)} ${losa.campos[campoLosa].unidad} @ (${x.toFixed(1)}, ${y.toFixed(1)}) m`;
}

function mostrarDiseno(el) {
  const kN = (n) => (n / 1000).toFixed(0);
  const est = el.estribo_txt ? ` · ${el.estribo_txt}` : '';
  const dem = el.tipo === 'columna'
    ? `Pu=${kN(el.demanda.pu)} kN, My=${kN(el.muy)} Mz=${kN(el.muz)} kN·m (u=${el.utilizacion.toFixed(2)})`
    : `Mu=${kN(el.demanda.mu)} kN·m, Vu=${kN(el.demanda.vu)} kN`;
  info.textContent = `${el.designacion} · combo ${el.combo} · ${dem}${est} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
}

function resumenEsfuerzos(id) {
  if (!esfuerzos) return null;
  const el = esfuerzos.elementos.find((e) => e.id === id);
  if (!el) return null;
  const N = -el.extremo_i[0];                       // tracción +
  const signo = N >= 0 ? 'tracción' : 'compresión';
  let mmax = 0;
  for (const fila of el.diagrama) {
    mmax = Math.max(mmax, Math.abs(fila[5]), Math.abs(fila[6]));   // |My|, |Mz|
  }
  const kN = (n) => (n / 1000).toFixed(0);
  const kNm = (n) => (n / 1000).toFixed(1);
  return `N = ${kN(Math.abs(N))} kN (${signo}) · |M|máx = ${kNm(mmax)} kN·m`;
}

function dibujarDiagramas2D(id) {
  if (!esfuerzos || !diagSvg) return;
  const el = esfuerzos.elementos.find((e) => e.id === id);
  if (!el) return;                      // id no encontrado: panel intacto
  diagSvg.replaceChildren(diagramaSVG(el));
}

function esfuerzosEnEstacion(el, s) {
  const filas = el && el.diagrama;
  if (!filas || !filas.length) return [0, 0, 0, 0, 0, 0];
  if (s <= filas[0][0]) return filas[0].slice(1);
  const ult = filas[filas.length - 1];
  if (s >= ult[0]) return ult.slice(1);
  for (let k = 0; k < filas.length - 1; k++) {
    const s0 = filas[k][0], s1 = filas[k + 1][0];
    if (s >= s0 && s <= s1) {
      const t = s1 > s0 ? (s - s0) / (s1 - s0) : 0;
      return filas[k].slice(1).map((v, c) => v + (filas[k + 1][c + 1] - v) * t);
    }
  }
  return ult.slice(1);
}

function datosSeccion(id, s, L) {
  const el = esfuerzos && esfuerzos.elementos.find((e) => e.id === id);
  const bar = barras.find((b) => b.id === id);
  if (!el || !bar) return null;
  const d = (diseno && diseno.elementos.find((e) => e.id === id))
         || (armado && armado.elementos.find((e) => e.id === id));
  return {
    b: bar.b, h: bar.h,
    long: d ? d.long : null,
    estribo: d ? d.estribo : null,
    designacion: d ? d.designacion : undefined,
    cumple: d ? d.cumple : undefined,
    fuerzas: esfuerzosEnEstacion(el, s),
    s, L,
  };
}

function construirAnilloSeccion(el) {
  disposeAnillo();
  const bar = barras.find((b) => b.id === el.id);
  if (!bar) return;
  const r = Math.max(bar.b, bar.h) * 0.7;
  const geo = new THREE.TorusGeometry(r, r * 0.06, 8, 32);
  anilloSeccion = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ color: 0x00ff88 }));
  scene.add(anilloSeccion);
}

function posicionarAnillo(s, el) {
  if (!anilloSeccion) return;
  const bar = barras.find((b) => b.id === el.id);
  const vi = basePos[bar.i], vj = basePos[bar.j];
  if (!vi || !vj) return;
  const L = el.longitud || vi.distanceTo(vj) || 1;
  anilloSeccion.position.copy(vi).lerp(vj, Math.max(0, Math.min(1, s / L)));
  anilloSeccion.lookAt(vj);   // eje del miembro = normal del toro (plano del corte ⟂ al eje)
}

function disposeAnillo() {
  if (!anilloSeccion) return;
  scene.remove(anilloSeccion);
  anilloSeccion.geometry.dispose();
  anilloSeccion.material.dispose();
  anilloSeccion = null;
}

function dibujarSeccion() {
  if (secElId == null || !esfuerzos) return;
  const el = esfuerzos.elementos.find((e) => e.id === secElId);
  if (!el) return;
  const L = el.longitud || 1;
  const s = parseFloat(secSlider.value);
  const datos = datosSeccion(secElId, s, L);
  if (!datos) return;
  secSvgActual = seccionSVG(datos);
  secHost.replaceChildren(secSvgActual);
  posicionarAnillo(s, el);
}

// --- Teardown: limpiar la escena para cargar otro modelo ---
function limpiarEscena() {
  for (const bar of barras) { scene.remove(bar.mesh); bar.mesh.geometry.dispose(); }
  barras.length = 0;
  for (const k of Object.keys(basePos)) delete basePos[k];

  if (losaMesh) { scene.remove(losaMesh); losaMesh.geometry.dispose(); losaMesh.material.dispose(); losaMesh = null; }
  if (armadoGroup) {
    scene.remove(armadoGroup);
    armadoGroup.traverse((o) => { if (o.geometry) o.geometry.dispose(); });
    armadoGroup = null;
  }
  disposeDiseno();
  disposeCintas();
  if (diagSvg) diagSvg.replaceChildren();          // limpiar panel 2D del modelo anterior
  diagComp = 0; selDiagComp.value = '0';           // reset del componente de cintas
  disposeAnillo();
  if (secHost) secHost.replaceChildren();
  if (secDiv) secDiv.style.display = 'none';
  secActivo = false; secElId = null; secSvgActual = null;

  resultados = null; esfuerzos = null; frameBbox = null;
  losa = null; armado = null; diseno = null;
  losaActiva = false; refuerzoActivo = false; disenoActivo = false; diagActivo = false;

  // Reconstruir el <select> dejando solo sin-deformar.
  selEstado.length = 0;
  selEstado.add(new Option('sin deformar', 'sin-deformar'));
  estado = 'sin-deformar';
}

// --- Render: construir barras + deformada/modos desde los DTOs ya obtenidos ---
function renderEscena({ escena, resultados: res, esfuerzos: esf }) {
  for (const n of escena.nodos) basePos[n.id] = new THREE.Vector3(n.p[0], n.p[1], n.p[2]);
  for (const b of escena.barras) {
    if (basePos[b.i] && basePos[b.j]) addBarra(b);
  }
  frameBbox = escena.bbox;
  encuadrar(escena.bbox.min, escena.bbox.max);
  setMsg(`${escena.barras.length} barras · ${escena.nodos.length} nodos`);

  if (res) {
    resultados = res;
    selEstado.add(new Option('deformada', 'deformada'));
    for (const m of resultados.modos) {
      selEstado.add(new Option('modo ' + m.indice, 'modo-' + m.indice));
    }
  }
  if (esf) {
    esfuerzos = esf;
    selEstado.add(new Option('diagramas', 'diagramas'));
    selEstado.add(new Option('sección', 'seccion'));
  }
}

// --- Carga (modo-ejemplo: el modelo del server) ---
async function cargar() {
  let escena;
  try {
    const r = await fetch('./escena');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    escena = await r.json();
  } catch (e) {
    setMsg('Error cargando /escena: ' + e.message);
    return;
  }

  let res = null;
  try {
    const r = await fetch('./resultados');
    if (r.ok) res = await r.json();
    else setMsg(msg.textContent + ' · sin resultados (HTTP ' + r.status + ')');
  } catch (e) {
    setMsg(msg.textContent + ' · sin resultados (' + e.message + ')');
  }

  let esf = null;
  try {
    const r = await fetch('./esfuerzos');
    if (r.ok) esf = await r.json();
  } catch (e) { /* sin esfuerzos: el pick readout queda inactivo */ }

  renderEscena({ escena, resultados: res, esfuerzos: esf });

  await cargarLosa();
  await cargarArmado();
  await cargarDiseno();
}

async function cargarLosa() {
  try {
    const r = await fetch('./losa');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    losa = await r.json();
  } catch (e) {
    return;
  }
  construirLosa();
  selEstado.add(new Option('losa: deflexión', 'losa-deflexion'));
  selEstado.add(new Option('losa: momento Mx', 'losa-momento_mx'));
  selEstado.add(new Option('losa: momento My', 'losa-momento_my'));
}

async function cargarArmado() {
  try {
    const r = await fetch('./armado');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    armado = await r.json();
  } catch (e) {
    return;
  }
  armadoGroup = construirJaula(armado, () => MAT_LONG);
  selEstado.add(new Option('refuerzo: armado', 'refuerzo'));
}

function fetchDisenoUrl() {
  const fc = parseFloat(inpFc.value) || 21;
  const fy = parseFloat(inpFy.value) || 420;
  const rec = parseFloat(inpRec.value) || 0.04;
  return `./diseno?fc=${fc}&fy=${fy}&rec=${rec}`;
}

function disposeDiseno() {
  if (!disenoGroup) return;
  scene.remove(disenoGroup);
  disenoGroup.traverse((o) => { if (o.geometry) o.geometry.dispose(); });
  disenoGroup = null;
}

async function cargarDiseno() {
  try {
    const r = await fetch(fetchDisenoUrl());
    if (!r.ok) throw new Error('HTTP ' + r.status);
    diseno = await r.json();
  } catch (e) {
    return;   // sin diseño: no se agrega el estado
  }
  disenoGroup = construirJaula(diseno, (el) => (el.cumple ? MAT_OK : MAT_FALLA));
  selEstado.add(new Option('diseño: armado', 'diseno'));
}

async function redisenar() {
  let nuevo;
  try {
    const r = await fetch(fetchDisenoUrl());
    if (!r.ok) throw new Error('HTTP ' + r.status);
    nuevo = await r.json();
  } catch (e) {
    info.textContent = 'rediseño: error (' + e.message + ')';
    return;   // mantiene la jaula previa
  }
  diseno = nuevo;
  disposeDiseno();
  disenoGroup = construirJaula(diseno, (el) => (el.cumple ? MAT_OK : MAT_FALLA));
  if (disenoActivo) entrarDiseno();
}

btnRedi.addEventListener('click', redisenar);

// --- WebXR ---
if (navigator.xr && navigator.xr.isSessionSupported) {
  navigator.xr.isSessionSupported('immersive-vr').then((ok) => {
    if (ok) document.body.appendChild(VRButton.createButton(renderer));
  });
}

const piso = new THREE.Mesh(
  new THREE.PlaneGeometry(500, 500).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ visible: false }));
scene.add(piso);

const marca = new THREE.Mesh(
  new THREE.CircleGeometry(0.25, 32).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ color: 0x00ff88 }));
marca.visible = false;
scene.add(marca);

const raycaster = new THREE.Raycaster();
const rotMatrix = new THREE.Matrix4();
let destino = null;

function crearControl(i) {
  const c = renderer.xr.getController(i);
  c.addEventListener('selectstart', () => { c.userData.activo = true; });
  c.addEventListener('selectend', () => {
    c.userData.activo = false;
    if (destino) {
      const cabeza = new THREE.Vector3().setFromMatrixPosition(camera.matrixWorld);
      rig.position.x += destino.x - cabeza.x;
      rig.position.z += destino.z - cabeza.z;
    }
  });
  c.add(new THREE.Line(
    new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, -5)]),
    new THREE.LineBasicMaterial({ color: 0x00ff88 })));
  rig.add(c);
  return c;
}
const xrControls = [crearControl(0), crearControl(1)];

function actualizarTeletransporte() {
  destino = null;
  marca.visible = false;
  for (const c of xrControls) {
    if (!c.userData.activo) continue;
    rotMatrix.identity().extractRotation(c.matrixWorld);
    raycaster.ray.origin.setFromMatrixPosition(c.matrixWorld);
    raycaster.ray.direction.set(0, 0, -1).applyMatrix4(rotMatrix);
    const hits = raycaster.intersectObject(piso);
    if (hits.length) {
      destino = hits[0].point;
      marca.position.copy(destino);
      marca.visible = true;
    }
  }
}

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

renderer.setAnimationLoop((time) => {
  const now = (time || 0) / 1000;
  if (lastT === null) lastT = now;
  const dt = now - lastT;
  lastT = now;
  if (playing) tAcum += dt;
  const fase = Math.sin((2 * Math.PI * tAcum) / T_DISPLAY);

  if (losaActiva) actualizarLosa();
  else actualizarBarras(fase);

  if (renderer.xr.isPresenting) actualizarTeletransporte();
  else controls.update();
  renderer.render(scene, camera);
});

cargar();

// Modo-custom: cargar un modelo propio reemplaza la escena (sin overlays de ejemplo).
crearShell({
  onModelo: (bundle) => {
    limpiarEscena();
    renderEscena(bundle);
    selEstado.value = 'sin-deformar';
    setEstado('sin-deformar');
  },
});

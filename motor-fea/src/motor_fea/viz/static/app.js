import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';

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
let frameBbox = null;

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

// --- Barras ---
function addBarra(b) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, 1), MAT[b.tipo] || MAT.viga);
  scene.add(mesh);
  barras.push({ mesh, i: b.i, j: b.j, id: b.id });
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
  if (losaMesh) losaMesh.visible = false;
  if (armadoGroup) armadoGroup.visible = false;
  if (disenoGroup) disenoGroup.visible = false;
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

function setEstado(nuevo) {
  const veniaEspecial = losaActiva || refuerzoActivo || disenoActivo;
  estado = nuevo;
  resetOverlays();
  if (nuevo.startsWith('losa-')) { entrarLosa(nuevo); return; }
  if (nuevo === 'refuerzo') { entrarRefuerzo(); return; }
  if (nuevo === 'diseno') { entrarDiseno(); return; }
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
exagInput.addEventListener('input', () => { exag = parseFloat(exagInput.value); });
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
  const dem = el.tipo === 'columna'
    ? `Pu=${kN(el.demanda.pu)} kN, Mu=${kN(el.demanda.mu)} kN·m`
    : `Mu=${kN(el.demanda.mu)} kN·m, Vu=${kN(el.demanda.vu)} kN`;
  info.textContent = `${el.designacion} · combo ${el.combo} · ${dem} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
}

// --- Carga ---
async function cargar() {
  let data;
  try {
    const r = await fetch('./escena');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    data = await r.json();
  } catch (e) {
    setMsg('Error cargando /escena: ' + e.message);
    return;
  }
  for (const n of data.nodos) basePos[n.id] = new THREE.Vector3(n.p[0], n.p[1], n.p[2]);
  for (const b of data.barras) {
    if (basePos[b.i] && basePos[b.j]) addBarra(b);
  }
  frameBbox = data.bbox;
  encuadrar(data.bbox.min, data.bbox.max);
  setMsg(`${data.barras.length} barras · ${data.nodos.length} nodos`);

  await cargarResultados();
  await cargarLosa();
  await cargarArmado();
  await cargarDiseno();
}

async function cargarResultados() {
  try {
    const r = await fetch('./resultados');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    resultados = await r.json();
  } catch (e) {
    setMsg(msg.textContent + ' · sin resultados (' + e.message + ')');
    return;
  }
  selEstado.add(new Option('deformada', 'deformada'));
  for (const m of resultados.modos) {
    selEstado.add(new Option('modo ' + m.indice, 'modo-' + m.indice));
  }
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

async function cargarDiseno() {
  try {
    const r = await fetch('./diseno');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    diseno = await r.json();
  } catch (e) {
    return;   // sin diseño: no se agrega el estado
  }
  disenoGroup = construirJaula(diseno, (el) => (el.cumple ? MAT_OK : MAT_FALLA));
  selEstado.add(new Option('diseño: armado', 'diseno'));
}

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

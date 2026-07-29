import { useState, useRef, useEffect } from 'react';
import { UploadCloud, CheckCircle, XCircle, AlertTriangle, RefreshCw } from 'lucide-react';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';

function App() {
  const [baseUrl, setBaseUrl] = useState('http://localhost:8000');
  const [isConnected, setIsConnected] = useState(null);
  const [isUploading, setIsUploading] = useState(false);
  const [elementos, setElementos] = useState([]);
  const [diseno, setDiseno] = useState([]);
  const [fc, setFc] = useState(21);
  const [fy, setFy] = useState(420);
  const [filtroTipo, setFiltroTipo] = useState('todos');
  const [esfuerzos, setEsfuerzos] = useState([]);
  const [esfuerzoElemId, setEsfuerzoElemId] = useState('');
  
  const iframeRef = useRef(null);

  const conectar = async () => {
    try {
      const res = await fetch(`${baseUrl}/escena`);
      if (res.ok) {
        setIsConnected(true);
        const data = await res.json();
        setElementos(data.elementos || data || []);
        fetchDiseno();
      } else {
        setIsConnected(false);
      }
    } catch (err) {
      setIsConnected(false);
    }
  };

  const fetchDiseno = async () => {
    try {
      const res = await fetch(`${baseUrl}/diseno?fc=${fc}&fy=${fy}`);
      if (res.ok) {
        const data = await res.json();
        setDiseno(data.resultados || data || []);
      }
    } catch (err) {
      console.error(err);
    }
  };

  const handleDragOver = (e) => {
    e.preventDefault();
  };

  const onDrop = async (e) => {
    e.preventDefault();
    const file = e.dataTransfer?.files[0] || e.target?.files?.[0];
    if (!file) return;
    
    setIsUploading(true);
    const text = await file.text();
    
    try {
      const res = await fetch(`${baseUrl}/visor-edificio`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: text
      });
      if (res.ok) {
         conectar();
      }
    } catch (err) {
      console.error(err);
    } finally {
      setIsUploading(false);
    }
  };

  const enviarIframe = (mensaje) => {
    if (iframeRef.current && iframeRef.current.contentWindow) {
      iframeRef.current.contentWindow.postMessage(mensaje, '*');
    }
  };

  const fetchEsfuerzos = async (id) => {
    setEsfuerzoElemId(id);
    if (!id) {
       setEsfuerzos([]);
       return;
    }
    try {
      const res = await fetch(`${baseUrl}/esfuerzos?id=${id}&n=11`);
      if (res.ok) {
        const data = await res.json();
        setEsfuerzos(Array.isArray(data) ? data : data.esfuerzos || data.puntos || []);
      }
    } catch (err) {
      console.error(err);
    }
  };

  const seleccionarElemento = (id) => {
    fetchEsfuerzos(id);
    enviarIframe({ tipo: 'seleccionar', id });
  };

  const elementosFiltrados = elementos.filter(e => filtroTipo === 'todos' || (e.tipo && e.tipo.toLowerCase() === filtroTipo));

  return (
    <div className="min-h-screen bg-[var(--color-page)] text-[var(--color-text-main)] font-sans flex flex-col">
      {/* SECCIÓN 1 – CABECERA */}
      <header className="bg-slate-900 border-b border-[var(--color-border-subtle)] px-6 py-4 flex flex-col sm:flex-row items-center justify-between shadow-md z-10 sticky top-0">
        <div className="flex items-center gap-2 mb-4 sm:mb-0">
          <div className="bg-[var(--color-text-main)] text-[#1e293b] font-bold text-xl px-3 py-1 rounded shadow-sm">
            EstructurasRD
          </div>
          <span className="text-sm text-slate-400 hidden sm:inline ml-2">Panel de Análisis FEA</span>
        </div>
        <div className="flex items-center gap-3">
          <input 
            type="text" 
            value={baseUrl}
            onChange={(e) => setBaseUrl(e.target.value)}
            className="bg-[var(--color-card)] border border-[var(--color-border-subtle)] rounded px-3 py-1.5 text-sm w-64 focus:outline-none focus:border-[var(--color-accent)] transition-colors"
            placeholder="URL del servidor"
          />
          <button 
            onClick={conectar}
            className="bg-[var(--color-accent)] hover:bg-blue-600 text-white px-4 py-1.5 rounded text-sm font-medium transition-colors flex items-center gap-2 shadow-sm"
          >
            Conectar
            {isConnected === true && <CheckCircle size={16} className="text-green-300" />}
            {isConnected === false && <XCircle size={16} className="text-red-300" />}
          </button>
        </div>
      </header>

      <main className="flex-1 max-w-[1600px] w-full mx-auto p-4 sm:p-6 grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        
        {/* LAYOUT MÓVIL: SECCIÓN 3 PRIMERO. DESKTOP: COLUMNA DERECHA (lg:order-2) */}
        <div className="lg:col-span-7 xl:col-span-8 flex flex-col gap-4 order-1 lg:order-2 lg:sticky lg:top-[88px]">
          {/* SECCIÓN 3 – VISOR 3D */}
          <div className="bg-[var(--color-card)] border border-[var(--color-border-subtle)] rounded-xl shadow-lg overflow-hidden flex flex-col h-[60vh] relative">
            <div className="p-3 border-b border-[var(--color-border-subtle)] flex items-center justify-center gap-3 bg-slate-800/50 backdrop-blur">
              <button 
                onClick={() => enviarIframe({ tipo: 'estado', valor: 'sin_deformar' })}
                className="px-4 py-1.5 text-sm font-medium bg-slate-700 hover:bg-slate-600 rounded-md transition-colors"
              >
                Sin deformar
              </button>
              <button 
                onClick={() => enviarIframe({ tipo: 'estado', valor: 'deformada' })}
                className="px-4 py-1.5 text-sm font-medium bg-slate-700 hover:bg-slate-600 rounded-md transition-colors"
              >
                Deformada
              </button>
              <button 
                onClick={() => enviarIframe({ tipo: 'estado', valor: 'modo_1' })}
                className="px-4 py-1.5 text-sm font-medium bg-slate-700 hover:bg-slate-600 rounded-md transition-colors"
              >
                Modo 1
              </button>
            </div>
            <div className="flex-1 bg-black relative">
              <iframe 
                ref={iframeRef}
                src={baseUrl} 
                className="w-full h-full border-none"
                title="Visor 3D"
              />
              {!isConnected && (
                <div className="absolute inset-0 flex items-center justify-center bg-[var(--color-card)]/90 backdrop-blur-sm z-10">
                  <div className="text-center">
                    <AlertTriangle size={48} className="mx-auto text-[var(--color-alert)] mb-4 opacity-80" />
                    <p className="text-slate-400">Conecta al servidor para visualizar el modelo 3D</p>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* COLUMNA IZQUIERDA (lg:order-1) */}
        <div className="lg:col-span-5 xl:col-span-4 flex flex-col gap-6 order-2 lg:order-1">
          
          {/* SECCIÓN 2 – CARGA DE EDIFICIO */}
          <section className="bg-[var(--color-card)] border border-[var(--color-border-subtle)] rounded-xl p-5 shadow-lg">
            <h2 className="text-lg font-semibold mb-4 text-[var(--color-text-main)] flex items-center gap-2">
              <UploadCloud size={20} className="text-[var(--color-accent)]" />
              Cargar Edificio Autorado
            </h2>
            <div 
              className={`border-2 border-dashed border-[var(--color-border-subtle)] rounded-lg p-8 text-center transition-all ${isUploading ? 'bg-slate-800' : 'hover:bg-slate-800/50 hover:border-slate-400'}`}
              onDragOver={handleDragOver}
              onDrop={onDrop}
            >
              {isUploading ? (
                <div className="flex flex-col items-center justify-center py-4">
                  <RefreshCw className="animate-spin text-[var(--color-accent)] mb-3" size={32} />
                  <p className="text-sm text-slate-300">Procesando archivo...</p>
                </div>
              ) : (
                <label className="cursor-pointer flex flex-col items-center justify-center">
                  <UploadCloud size={36} className="text-slate-400 mb-3" />
                  <p className="text-sm text-slate-300 mb-1">Arrastra un archivo <span className="font-mono text-xs bg-slate-800 px-1 py-0.5 rounded text-blue-300">.json</span> aquí</p>
                  <p className="text-xs text-slate-500">o haz clic para seleccionar</p>
                  <input type="file" accept=".json" className="hidden" onChange={onDrop} />
                </label>
              )}
            </div>
            <p className="text-xs text-slate-400 mt-3 text-center">
              Si no se carga nada, se usará el modelo por defecto.
            </p>
          </section>

          {/* SECCIÓN 4 – RESULTADOS DE DISEÑO */}
          <section className="bg-[var(--color-card)] border border-[var(--color-border-subtle)] rounded-xl shadow-lg flex flex-col max-h-[400px]">
            <div className="p-5 border-b border-[var(--color-border-subtle)]">
              <h2 className="text-lg font-semibold mb-4 text-[var(--color-text-main)]">Resultados de Diseño (ACI 318)</h2>
              <div className="flex gap-3 items-end">
                <div className="flex-1">
                  <label className="block text-xs text-slate-400 mb-1">f'c (MPa)</label>
                  <input 
                    type="number" 
                    value={fc} 
                    onChange={e => setFc(e.target.value)}
                    className="w-full bg-slate-900 border border-[var(--color-border-subtle)] rounded px-3 py-1.5 text-sm focus:outline-none focus:border-[var(--color-accent)]"
                  />
                </div>
                <div className="flex-1">
                  <label className="block text-xs text-slate-400 mb-1">fy (MPa)</label>
                  <input 
                    type="number" 
                    value={fy} 
                    onChange={e => setFy(e.target.value)}
                    className="w-full bg-slate-900 border border-[var(--color-border-subtle)] rounded px-3 py-1.5 text-sm focus:outline-none focus:border-[var(--color-accent)]"
                  />
                </div>
                <button 
                  onClick={fetchDiseno}
                  className="bg-slate-700 hover:bg-slate-600 text-white px-4 py-1.5 rounded text-sm font-medium transition-colors"
                >
                  Recalcular
                </button>
              </div>
            </div>
            <div className="overflow-auto p-0 flex-1 custom-scrollbar">
              <table className="w-full text-sm text-left">
                <thead className="text-xs text-slate-400 bg-slate-800/80 sticky top-0 backdrop-blur-sm">
                  <tr>
                    <th className="px-4 py-2 font-medium">Elem</th>
                    <th className="px-4 py-2 font-medium">Tipo</th>
                    <th className="px-4 py-2 font-medium">As (cm²)</th>
                    <th className="px-4 py-2 font-medium">ρ (%)</th>
                    <th className="px-4 py-2 font-medium">Estado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--color-border-subtle)]">
                  {diseno.map((item, i) => {
                    const isOk = item.rho >= 1 && item.rho <= 4;
                    return (
                      <tr key={i} className="hover:bg-slate-800/50 transition-colors">
                        <td className="px-4 py-2 font-mono text-xs">{item.id || item.elem}</td>
                        <td className="px-4 py-2">{item.tipo}</td>
                        <td className="px-4 py-2">{item.as?.toFixed(2)}</td>
                        <td className="px-4 py-2">{item.rho?.toFixed(2)}</td>
                        <td className="px-4 py-2">
                          {isOk ? (
                            <span className="inline-flex items-center gap-1 text-green-400 text-xs font-medium"><CheckCircle size={12}/> OK</span>
                          ) : (
                            <span className="inline-flex items-center gap-1 text-[var(--color-alert)] text-xs font-medium"><AlertTriangle size={12}/> Revisar</span>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                  {diseno.length === 0 && (
                    <tr>
                      <td colSpan="5" className="px-4 py-8 text-center text-slate-500">
                        No hay datos de diseño disponibles
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

          {/* SECCIÓN 5 – TABLA DE ELEMENTOS */}
          <section className="bg-[var(--color-card)] border border-[var(--color-border-subtle)] rounded-xl shadow-lg flex flex-col max-h-[350px]">
            <div className="p-4 border-b border-[var(--color-border-subtle)] flex items-center justify-between gap-4">
              <h2 className="text-lg font-semibold text-[var(--color-text-main)]">Elementos</h2>
              <div className="flex gap-1 bg-slate-900 rounded-md p-1 border border-[var(--color-border-subtle)]">
                {['todos', 'columna', 'viga'].map(tipo => (
                  <button
                    key={tipo}
                    onClick={() => setFiltroTipo(tipo)}
                    className={`px-3 py-1 text-xs font-medium rounded capitalize transition-all ${filtroTipo === tipo ? 'bg-[var(--color-accent)] text-white shadow-sm' : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800'}`}
                  >
                    {tipo}
                  </button>
                ))}
              </div>
            </div>
            <div className="overflow-auto p-0 flex-1 custom-scrollbar">
              <table className="w-full text-sm text-left">
                <thead className="text-xs text-slate-400 bg-slate-800/80 sticky top-0 backdrop-blur-sm">
                  <tr>
                    <th className="px-4 py-2 font-medium">ID</th>
                    <th className="px-4 py-2 font-medium">Tipo</th>
                    <th className="px-4 py-2 font-medium">Nodo i</th>
                    <th className="px-4 py-2 font-medium">Nodo j</th>
                    <th className="px-4 py-2 font-medium">L (m)</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[var(--color-border-subtle)]">
                  {elementosFiltrados.map((item, i) => (
                    <tr 
                      key={item.id || i} 
                      onClick={() => seleccionarElemento(item.id)}
                      className="hover:bg-slate-800 transition-colors cursor-pointer"
                    >
                      <td className="px-4 py-2 font-mono text-xs">{item.id}</td>
                      <td className="px-4 py-2 capitalize">{item.tipo}</td>
                      <td className="px-4 py-2 font-mono text-xs text-slate-400">{item.nodo_i || item.nodoI}</td>
                      <td className="px-4 py-2 font-mono text-xs text-slate-400">{item.nodo_j || item.nodoJ}</td>
                      <td className="px-4 py-2">{item.l?.toFixed(2) || item.L?.toFixed(2)}</td>
                    </tr>
                  ))}
                  {elementosFiltrados.length === 0 && (
                    <tr>
                      <td colSpan="5" className="px-4 py-8 text-center text-slate-500">
                        No hay elementos para mostrar
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>

          {/* SECCIÓN 6 – DIAGRAMA DE ESFUERZOS */}
          <section className="bg-[var(--color-card)] border border-[var(--color-border-subtle)] rounded-xl shadow-lg p-5">
            <div className="flex items-center justify-between mb-4">
              <h2 className="text-lg font-semibold text-[var(--color-text-main)]">Diagrama de Esfuerzos</h2>
              <select 
                value={esfuerzoElemId}
                onChange={e => fetchEsfuerzos(e.target.value)}
                className="bg-slate-900 border border-[var(--color-border-subtle)] rounded px-3 py-1.5 text-sm focus:outline-none focus:border-[var(--color-accent)] max-w-[150px]"
              >
                <option value="">Seleccione...</option>
                {elementos.map(e => (
                  <option key={e.id} value={e.id}>{e.id} ({e.tipo})</option>
                ))}
              </select>
            </div>
            
            <div className="h-48 w-full">
              {esfuerzos && esfuerzos.length > 0 ? (
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={esfuerzos} margin={{ top: 5, right: 5, left: -20, bottom: 5 }} layout="vertical">
                    <CartesianGrid strokeDasharray="3 3" stroke="#334155" horizontal={false} />
                    <XAxis type="number" stroke="#94a3b8" fontSize={10} />
                    <YAxis dataKey="x" type="category" stroke="#94a3b8" fontSize={10} />
                    <Tooltip 
                      contentStyle={{ backgroundColor: '#1e293b', borderColor: '#334155', borderRadius: '8px', fontSize: '12px' }}
                      itemStyle={{ color: '#f8fafc' }}
                    />
                    <Legend wrapperStyle={{ fontSize: '12px' }} />
                    <Bar dataKey="V" name="Cortante (V)" fill="#3b82f6" radius={[0, 4, 4, 0]} />
                    <Bar dataKey="M" name="Momento (M)" fill="#f97316" radius={[0, 4, 4, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              ) : (
                <div className="h-full flex items-center justify-center border border-dashed border-[var(--color-border-subtle)] rounded-lg text-slate-500 text-sm">
                  {esfuerzoElemId ? 'No hay datos de esfuerzos' : 'Seleccione un elemento para ver su diagrama'}
                </div>
              )}
            </div>
          </section>

        </div>
      </main>
      
      <style dangerouslySetInnerHTML={{__html: `
        .custom-scrollbar::-webkit-scrollbar { width: 6px; }
        .custom-scrollbar::-webkit-scrollbar-track { background: transparent; }
        .custom-scrollbar::-webkit-scrollbar-thumb { background: #334155; border-radius: 3px; }
        .custom-scrollbar::-webkit-scrollbar-thumb:hover { background: #475569; }
      `}} />
    </div>
  );
}

export default App;

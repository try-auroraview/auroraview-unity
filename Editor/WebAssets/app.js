(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const status = (message, state = '') => { byId('status').textContent = message; byId('status').className = `status ${state}`; };
  const activity = message => {
    const item = document.createElement('li');
    const time = document.createElement('time'); time.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    const text = document.createElement('span'); text.textContent = message;
    item.append(time, text); byId('activity').prepend(item);
    while (byId('activity').children.length > 5) byId('activity').lastElementChild.remove();
  };
  function render(context) {
    byId('scene').textContent = context.scene || 'Untitled';
    byId('version').textContent = context.unityVersion;
    byId('thread').textContent = `#${context.mainThreadId} · PID ${context.processId}`;
    byId('selection').replaceChildren();
    for (const object of context.selection || []) {
      const item = document.createElement('li');
      const name = document.createElement('span'); name.className = 'object-name'; name.textContent = object.name;
      const detail = document.createElement('span'); detail.className = 'object-info'; detail.textContent = `ID ${object.objectId} · (${object.x}, ${object.y}, ${object.z})`; name.append(detail);
      const button = document.createElement('button'); button.className = 'quiet'; button.textContent = 'Select';
      button.addEventListener('click', () => invoke('scene.select', { objectId: object.objectId }));
      item.append(name, button); byId('selection').append(item);
    }
    if (!byId('selection').children.length) { const item = document.createElement('li'); item.className = 'empty'; item.textContent = 'Select an object in Unity, or create a cube above.'; byId('selection').append(item); }
  }
  async function invoke(method, params) {
    try { const result = await window.auroraview.call(method, params); render(result); status('Connected · calls execute on the Unity Editor main thread', 'ready'); activity(`${method} completed${result.created ? ` · ${result.created.name}` : ''}`); return result; }
    catch (error) { status(error.message, 'error'); activity(`${method} refused · ${error.message}`); throw error; }
  }
  function ready() {
    if (!window.auroraview?._ready) { status('Native AuroraView bridge is unavailable. Open this page from the Unity panel.', 'error'); return; }
    byId('create').disabled = false; byId('refresh').disabled = false;
    byId('create').addEventListener('click', async () => { byId('create').disabled = true; try { await invoke('scene.create_cube', { name: byId('cube-name').value }); } catch {} finally { byId('create').disabled = false; } });
    byId('refresh').addEventListener('click', () => invoke('scene.context').catch(() => {}));
    window.auroraview.on('scene.selection', render);
    invoke('scene.context').then(() => window.ipc.postMessage('auroraview:browser-ready')).catch(() => {});
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, { once: true }); else ready();
})();

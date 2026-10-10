(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const buttons = ['project', 'console', 'dirty', 'create'].map(byId);
  function show(method, result) {
    byId('result').textContent = JSON.stringify(result, null, 2);
    byId('summary').textContent = method === 'editor.inspect_dirty_assets'
      ? `${result.items_count} of ${result.dirty_persistent_count ?? 'unknown'} dirty assets returned · ${result.complete === true ? 'complete enumeration' : 'incomplete enumeration'} · ${result.identity_complete === true ? 'identities available' : 'some identities unknown'}`
      : method;
    byId('status').textContent = result.error_code ? `Diagnostic refused: ${result.error_code}` : 'Command completed';
    byId('status').className = result.error_code ? 'status error' : 'status ready';
  }
  async function invoke(method, params = {}) {
    let created;
    buttons.forEach(button => { button.disabled = true; });
    byId('status').textContent = 'Reading the Editor…';
    try {
      let result = await window.auroraview.call(method, params);
      if (method === 'scene.create_game_object') {
        created = result;
        result = await window.auroraview.call('scene.inspect_game_object', { instance_id: result.instance_id });
      }
      show(method, result);
      return result;
    } catch (error) {
      byId('status').textContent = error.message;
      byId('status').className = 'status error';
      byId('summary').textContent = created ? 'Object created; readback failed. Inspect the Editor before retrying.' : `${method} failed`;
      byId('result').textContent = created ? JSON.stringify(created, null, 2) : '';
    } finally { buttons.forEach(button => { button.disabled = false; }); }
  }
  function ready() {
    if (!window.auroraview?._ready) { byId('status').textContent = 'Open this page from the Unity DCC-MCP Scene Tools menu.'; return; }
    byId('project').addEventListener('click', () => invoke('project.inspect'));
    byId('console').addEventListener('click', () => invoke('editor.read_console', { limit: 20 }));
    byId('dirty').addEventListener('click', () => invoke('editor.inspect_dirty_assets'));
    byId('create').addEventListener('click', () => invoke('scene.create_game_object', { name: byId('object-name').value }));
    invoke('project.inspect');
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, { once: true }); else ready();
})();

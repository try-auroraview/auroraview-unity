import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../Editor/WebAssets/dcc-mcp.js', import.meta.url), 'utf8');
const settle = () => new Promise(resolve => setImmediate(resolve));
async function panel(reply, ready = true) {
  const elements = new Map();
  const calls = [];
  const get = id => {
    if (!elements.has(id)) elements.set(id, { disabled: true, value: 'Test Object', listeners: {},
      textContent: '', className: '', addEventListener(event, callback) { this.listeners[event] = callback; },
      set innerHTML(value) { throw new Error(`Unexpected HTML rendering: ${value}`); } });
    return elements.get(id);
  };
  vm.runInNewContext(source, {
    document: { readyState: 'complete', getElementById: get },
    window: { auroraview: { _ready: ready, async call(method, params) { calls.push({ method, params }); return reply(method, params); } } },
  });
  await settle();
  return { get, calls, async click(id) { get(id).listeners.click(); await settle(); } };
}

test('startup only inspects the project; complete dirty results retain every opaque identity', async () => {
  const items = Array.from({ length: 539 }, (_, index) => ({ object_id: `900719925474099${index}`, name: '<script>unsafe</script>' }));
  const ui = await panel(method => method === 'project.inspect' ? { is_compiling: false } : {
    dirty_persistent_count: 539, items_count: 539, complete: true, identity_complete: false, items,
  });
  assert.deepEqual(ui.calls.map(call => call.method), ['project.inspect']);
  await ui.click('dirty');
  assert.equal(ui.calls[1].method, 'editor.inspect_dirty_assets');
  assert.equal(JSON.stringify(ui.calls[1].params), '{}');
  assert.match(ui.get('summary').textContent, /539 of 539.*complete enumeration.*some identities unknown/);
  assert.deepEqual(JSON.parse(ui.get('result').textContent).items, items);
});

test('response budget refusal remains incomplete and keeps its true total', async () => {
  const ui = await panel(method => method === 'project.inspect' ? {} : {
    dirty_persistent_count: 539, items_count: 0, complete: false, identity_complete: false,
    error_code: 'response_budget_exceeded', items: [],
  });
  await ui.click('dirty');
  assert.match(ui.get('summary').textContent, /0 of 539.*incomplete enumeration/);
  assert.match(ui.get('status').textContent, /response_budget_exceeded/);
  assert.equal(ui.get('create').disabled, false);
});

test('creation uses the official command and reads back the exact string identity', async () => {
  const id = '9223372036854775807';
  const ui = await panel(method => method === 'scene.create_game_object' ? { created: true, instance_id: id } : { instance_id: id, name: 'Test Object' });
  await ui.click('create');
  assert.deepEqual(ui.calls.map(call => call.method), ['project.inspect', 'scene.create_game_object', 'scene.inspect_game_object']);
  assert.equal(ui.calls[1].params.name, 'Test Object');
  assert.equal(ui.calls[2].params.instance_id, id);
  assert.equal(JSON.parse(ui.get('result').textContent).instance_id, id);
});

test('failed readback preserves the successful creation receipt instead of inviting a retry', async () => {
  const created = { created: true, instance_id: '-421', name: 'Test Object' };
  const ui = await panel(method => {
    if (method === 'scene.create_game_object') return created;
    if (method === 'scene.inspect_game_object') throw new Error('Editor unavailable');
    return {};
  });
  await ui.click('create');
  assert.deepEqual(JSON.parse(ui.get('result').textContent), created);
  assert.match(ui.get('summary').textContent, /Object created; readback failed/);
  assert.equal(ui.get('status').textContent, 'Editor unavailable');
});

test('a page opened without the native bridge cannot execute commands', async () => {
  const ui = await panel(() => { throw new Error('Must not call'); }, false);
  assert.equal(ui.calls.length, 0);
  assert.equal(ui.get('create').disabled, true);
});

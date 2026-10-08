import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import crypto from 'node:crypto';

const source = fs.readFileSync(new URL('../Editor/WebAssets/vendor/event_bridge.js', import.meta.url), 'utf8');
function bridge() {
  const calls = [];
  const window = { location: { href: 'file:///auroraview-unity/index.html' }, ipc: { postMessage: text => calls.push(JSON.parse(text)) }, addEventListener() {}, dispatchEvent() {} };
  const document = { dispatchEvent() {}, readyState: 'complete', addEventListener() {} };
  const context = { window, document, console, setTimeout, clearTimeout, setInterval, clearInterval, Event: class {}, CustomEvent: class {}, Map, Set, Proxy };
  vm.runInNewContext(source, context);
  return { api: window.auroraview, calls };
}
test('vendored upstream bridge has the documented source digest', () => {
  assert.equal(crypto.createHash('sha256').update(source).digest('hex'), '4550b027e400c6500fca5e64dd8c50348bde15848c513d2738dd6e443b4a17ff');
});
test('official bridge call/result round trip uses the Unity contract envelope', async () => {
  const { api, calls } = bridge();
  const promise = api.call('scene.context');
  const request = calls.find(item => item.type === 'call');
  assert.equal(request.method, 'scene.context');
  api.trigger('__auroraview_call_result', { id: request.id, ok: true, result: { scene: 'Demo', mainThreadId: 1 } });
  assert.equal((await promise).scene, 'Demo');
});
test('official bridge rejects host errors and delivers named events', async () => {
  const { api, calls } = bridge();
  let selected;
  const unsubscribe = api.on('scene.selection', value => { selected = value; });
  api.trigger('scene.selection', { objectId: 42 });
  assert.equal(selected.objectId, 42); unsubscribe();
  const promise = api.call('scene.select', { objectId: 0 });
  const request = calls.find(item => item.type === 'call');
  api.trigger('__auroraview_call_result', { id: request.id, ok: false, error: { code: 'INVALID_REQUEST', message: 'Not a scene object' } });
  await assert.rejects(promise, /Not a scene object/);
});

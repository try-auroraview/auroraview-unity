import test from 'node:test';
import assert from 'node:assert/strict';
import { handle, tools } from '../agent/server.mjs';

test('MCP discovers only explicitly registered scene contracts', async () => {
  const response = await handle({ id: 1, method: 'tools/list' }, () => { throw new Error('No Editor needed for discovery'); });
  assert.deepEqual(response.result.tools.map(tool => tool.name), ['unity_scene_context', 'unity_create_cube', 'unity_select_object']);
  assert.ok(response.result.tools.every(tool => !('method' in tool)));
});
test('MCP forwards context and mutations through the same scene contract', async () => {
  const received = [];
  for (const tool of tools) {
    const args = tool.method === 'scene.select' ? { objectId: 123 } : {};
    const response = await handle({ id: 7, method: 'tools/call', params: { name: tool.name, arguments: args } }, async payload => {
      received.push(payload); return { id: payload.id, ok: true, result: { mainThreadId: 1 } };
    });
    assert.equal(response.result.isError, false);
  }
  assert.deepEqual(received.map(item => item.method), ['scene.context', 'scene.create_cube', 'scene.select']);
});
test('unregistered methods and invalid arguments never reach the host', async () => {
  for (const params of [{ name: 'execute_code' }, { name: 'unity_create_cube', arguments: { script: 'bad' } },
    { name: 'unity_select_object', arguments: { objectId: '123' } }, { name: 'unity_create_cube', arguments: { name: '' } }]) {
    const response = await handle({ id: 2, method: 'tools/call', params }, () => assert.fail('must not dispatch'));
    assert.equal(response.result.isError, true);
  }
});
test('host refusal is an MCP tool error', async () => {
  const response = await handle({ id: 3, method: 'tools/call', params: { name: 'unity_create_cube' } }, async () => ({ ok: false, error: { message: 'Play mode' } }));
  assert.equal(response.result.isError, true);
  assert.match(response.result.content[0].text, /Play mode/);
});
test('initialization advertises tools and notifications have no reply', async () => {
  assert.ok((await handle({ id: 1, method: 'initialize' })).result.capabilities.tools);
  assert.equal(await handle({ method: 'notifications/initialized' }), null);
});
test('invalid JSON-RPC values produce a protocol refusal', async () => {
  for (const request of [null, 4, [], {}, { id: 1, method: 2 }]) {
    assert.equal((await handle(request)).error.code, -32600);
  }
});

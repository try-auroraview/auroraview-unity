import fs from 'node:fs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';
const pid = process.argv[process.argv.indexOf('--pid') + 1];
const output = process.argv[process.argv.indexOf('--output') + 1];
const server = spawn(process.execPath, [fileURLToPath(new URL('./server.mjs', import.meta.url)), '--pid', pid], { stdio: ['pipe', 'pipe', 'inherit'] });
const pending = new Map();
readline.createInterface({ input: server.stdout }).on('line', line => {
  const response = JSON.parse(line);
  const item = pending.get(response.id);
  if (item) { clearTimeout(item.timer); pending.delete(response.id); item.resolve(response); }
});
const rpc = (id, method, params) => new Promise((resolve, reject) => {
  const timer = setTimeout(() => { pending.delete(id); reject(new Error(`MCP stdio timeout: ${method}`)); }, 15000);
  pending.set(id, { resolve, timer });
  server.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
});
const call = async (id, name, args = {}) => {
  const result = await rpc(id, 'tools/call', { name, arguments: args });
  assert.equal(result.result.isError, false, result.result.content[0].text);
  return JSON.parse(result.result.content[0].text);
};
try {
await rpc(0, 'initialize', { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'auroraview-acceptance', version: '0.1.0' } });
const discovered = await rpc(5, 'tools/list', {});
assert.deepEqual(discovered.result.tools.map(tool => tool.name), ['unity_scene_context', 'unity_create_cube', 'unity_select_object']);
const context = await call(1, 'unity_scene_context');
assert.equal(context.processId, Number(pid));
const created = await call(2, 'unity_create_cube', { name: 'AuroraView MCP 验收立方体' });
assert.ok(created.created?.objectId);
assert.equal(created.created.name, 'AuroraView MCP 验收立方体');
const selected = await call(3, 'unity_select_object', { objectId: created.created.objectId });
assert.equal(selected.selection[0].objectId, created.created.objectId);
assert.equal(selected.mainThreadId, context.mainThreadId);
const readback = await call(4, 'unity_scene_context');
assert.equal(readback.selection[0].objectId, created.created.objectId);
const receipt = { processId: Number(pid), mainThreadId: context.mainThreadId, unityVersion: context.unityVersion,
  objectId: created.created.objectId, mcpContext: true, mcpCreate: true, mcpSelect: true, selectionReadback: true };
fs.writeFileSync(output, `${JSON.stringify(receipt, null, 2)}\n`);
process.stdout.write('PASS: MCP tools → current-user pipe → real Unity scene → selection readback\n');
} finally { server.stdin.end(); server.kill(); }

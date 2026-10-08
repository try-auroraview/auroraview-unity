#!/usr/bin/env node
import net from 'node:net';
import readline from 'node:readline';
import { pathToFileURL } from 'node:url';

export const tools = [
  { name: 'unity_scene_context', description: 'Read active Unity scene, selection, Editor version and main-thread identity.', inputSchema: { type: 'object', properties: {}, additionalProperties: false }, annotations: { readOnlyHint: true }, method: 'scene.context' },
  { name: 'unity_create_cube', description: 'Create a cube in the active Unity Editor scene with Undo; disabled in Play mode.', inputSchema: { type: 'object', properties: { name: { type: 'string', minLength: 1, maxLength: 80 } }, additionalProperties: false }, annotations: { readOnlyHint: false, destructiveHint: false }, method: 'scene.create_cube' },
  { name: 'unity_select_object', description: 'Select a live scene GameObject by the objectId returned by context/create.', inputSchema: { type: 'object', properties: { objectId: { type: 'integer' } }, required: ['objectId'], additionalProperties: false }, annotations: { readOnlyHint: false, destructiveHint: false }, method: 'scene.select' }
];

export function pipeCall(pid, payload) {
  if (!/^\d+$/.test(String(pid)) || Number(pid) < 1) throw new Error('An explicit positive Unity Editor PID is required.');
  return new Promise((resolve, reject) => {
    let attempts = 0;
    const connect = () => {
    const socket = net.connect(`\\\\.\\pipe\\auroraview-unity-${pid}`);
    socket.setEncoding('utf8');
    let connected = false;
    let data = '';
    const finish = error => { socket.destroy(); if (error) reject(error); };
    socket.setTimeout(12000, () => finish(new Error('Unity endpoint timed out.')));
    socket.on('error', error => {
      if (!connected && ['ENOENT', 'EBUSY'].includes(error.code) && attempts++ < 20) setTimeout(connect, 25);
      else reject(error);
    });
    socket.on('connect', () => { connected = true; socket.write(`${JSON.stringify(payload)}\n`); });
    socket.on('data', chunk => {
      data += chunk;
      if (data.length > 65536) return finish(new Error('Unity response exceeds 64 KiB.'));
      const newline = data.indexOf('\n');
      if (newline < 0) return;
      try { resolve(JSON.parse(data.slice(0, newline))); finish(); } catch (error) { finish(error); }
    });
    socket.on('end', () => { if (!data.includes('\n')) finish(new Error('Unity closed the endpoint without a response.')); });
    };
    connect();
  });
}

export async function handle(request, transport) {
  if (!request || typeof request !== 'object' || Array.isArray(request) || typeof request.method !== 'string')
    return { jsonrpc: '2.0', id: null, error: { code: -32600, message: 'Invalid JSON-RPC request.' } };
  if (request.id === undefined) return null;
  const response = { jsonrpc: '2.0', id: request.id };
  try {
    switch (request.method) {
      case 'initialize':
        response.result = { protocolVersion: '2024-11-05', capabilities: { tools: {} }, serverInfo: { name: 'auroraview-unity', version: '0.1.0' } };
        break;
      case 'ping': response.result = {}; break;
      case 'tools/list': response.result = { tools: tools.map(({ method, ...tool }) => tool) }; break;
      case 'tools/call': {
        const tool = tools.find(item => item.name === request.params?.name);
        if (!tool) throw new Error('Tool is not registered.');
        const args = request.params.arguments ?? {};
        if (typeof args !== 'object' || Array.isArray(args) || args === null) throw new Error('Arguments must be an object.');
        if (Object.keys(args).some(key => !Object.hasOwn(tool.inputSchema.properties, key))) throw new Error('Unexpected argument.');
        if (tool.method === 'scene.create_cube' && args.name !== undefined && (typeof args.name !== 'string' || args.name.trim().length < 1 || args.name.length > 80)) throw new Error('name must contain 1–80 characters.');
        if (tool.method === 'scene.select' && !Number.isInteger(args.objectId)) throw new Error('objectId must be an integer.');
        const result = await transport({ type: 'call', id: `mcp-${request.id}`, method: tool.method, params: args });
        response.result = { content: [{ type: 'text', text: JSON.stringify(result.ok ? result.result : result.error) }], isError: !result.ok };
        break;
      }
      default: return { ...response, error: { code: -32601, message: 'Method not found.' } };
    }
  } catch (error) {
    if (request.method === 'tools/call') response.result = { content: [{ type: 'text', text: error.message }], isError: true };
    else response.error = { code: -32602, message: error.message };
  }
  return response;
}

export async function run(pid, input = process.stdin, output = process.stdout) {
  if (!/^\d+$/.test(String(pid)) || Number(pid) < 1) throw new Error('Usage: node agent/server.mjs --pid <UnityEditorPID>');
  for await (const line of readline.createInterface({ input, crlfDelay: Infinity })) {
    if (line.length > 65536) { output.write(`${JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Request exceeds 64 KiB.' } })}\n`); continue; }
    let request;
    try { request = JSON.parse(line); }
    catch { output.write(`${JSON.stringify({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Invalid JSON.' } })}\n`); continue; }
    const response = await handle(request, payload => pipeCall(pid, payload));
    if (response) output.write(`${JSON.stringify(response)}\n`);
  }
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const index = process.argv.indexOf('--pid');
  run(index >= 0 ? process.argv[index + 1] : null).catch(error => { process.stderr.write(`${error.message}\n`); process.exitCode = 1; });
}

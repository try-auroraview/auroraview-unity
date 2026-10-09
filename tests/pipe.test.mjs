import test from 'node:test';
import assert from 'node:assert/strict';
import net from 'node:net';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('Core one-shot pipe client preserves request and session; no MCP server starts', { skip: process.platform !== 'win32' }, async () => {
  const pid = process.pid;
  const path = `\\\\.\\pipe\\auroraview-unity-${pid}`;
  const request = { type: 'call', id: 'core-pipe-test', method: 'scene.context', sessionId: 'a'.repeat(32), params: {} };
  let received;
  const server = net.createServer(socket => {
    let data = '';
    socket.setEncoding('utf8');
    socket.on('data', chunk => {
      data += chunk;
      if (!data.includes('\n')) return;
      received = JSON.parse(data.split('\n')[0]);
      socket.end(`${JSON.stringify({ id: received.id, ok: true, result: { sessionId: received.sessionId } })}\n`);
    });
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(path, resolve); });
  try {
    const child = spawn(process.execPath, [fileURLToPath(new URL('../agent/pipe-call.mjs', import.meta.url)), '--pid', String(pid)], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let output = '', errors = '';
    child.stdout.setEncoding('utf8').on('data', chunk => output += chunk);
    child.stderr.setEncoding('utf8').on('data', chunk => errors += chunk);
    const exited = new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    child.stdin.end(JSON.stringify(request));
    const timer = setTimeout(() => child.kill(), 5000);
    try { assert.equal(await exited, 0, errors); } finally { clearTimeout(timer); }
    assert.deepEqual(received, request);
    assert.deepEqual(JSON.parse(output), { id: request.id, ok: true, result: { sessionId: request.sessionId } });
  } finally {
    await new Promise(resolve => server.close(resolve));
  }
});

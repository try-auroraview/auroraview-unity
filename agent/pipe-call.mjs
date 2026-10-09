#!/usr/bin/env node
// A single bounded host call. MCP, discovery and lifecycle belong to Core.
import { pipeCall } from './server.mjs';

const index = process.argv.indexOf('--pid');
const pid = index >= 0 ? process.argv[index + 1] : null;
let input = '';
try {
  process.stdin.setEncoding('utf8');
  for await (const chunk of process.stdin) {
    input += chunk;
    if (Buffer.byteLength(input, 'utf8') > 65536) throw new Error('Request exceeds 64 KiB.');
  }
  const result = await pipeCall(pid, JSON.parse(input));
  process.stdout.write(JSON.stringify(result));
} catch (error) {
  process.stderr.write(`${error.message}\n`);
  process.exitCode = 1;
}

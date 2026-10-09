import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

test('package excludes Python caches and retains source files and metadata', { skip: process.platform !== 'win32' }, () => {
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'auroraview-package-')));
  const files = [
    'package.json', 'Editor/SceneContracts.cs', 'docs/core-runtime.md',
    'README.md', 'README.zh-CN.md', 'LICENSE', 'THIRD_PARTY.md',
    'agent/core.py', 'agent/core.py.meta', 'agent/pipe-call.mjs', 'agent/pipe-call.mjs.meta',
    'agent/requirements-core.txt', 'agent/requirements-core.txt.meta',
    'agent/nested/helper.py', 'agent/nested/helper.py.meta',
    'build/deps/webview2/LICENSE.txt',
  ];
  const caches = [
    'agent/__pycache__/core.cpython-311.pyc', 'agent/nested/__pycache__/helper.pyc',
    'agent/old.pyc', 'agent/nested/old.pyo', 'agent/__pycache__.meta',
    'agent/nested/__pycache__.meta', 'agent/old.pyc.meta', 'agent/nested/old.pyo.meta',
  ];
  try {
    for (const file of [...files, ...caches]) {
      const path = join(root, file);
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, file);
    }
    mkdirSync(join(root, 'scripts'));
    copyFileSync(fileURLToPath(new URL('../scripts/package.ps1', import.meta.url)), join(root, 'scripts/package.ps1'));
    execFileSync('powershell.exe', ['-NoProfile', '-File', join(root, 'scripts/package.ps1')], { windowsHide: true, stdio: 'pipe' });
    const outputDirectory = join(root, 'build/package');
    const archives = readdirSync(outputDirectory).filter(name => name.endsWith('.zip'));
    assert.equal(archives.length, 1);
    const archive = join(outputDirectory, archives[0]);
    const output = execFileSync('powershell.exe', ['-NoProfile', '-Command',
      'Add-Type -AssemblyName System.IO.Compression.FileSystem; $z = [IO.Compression.ZipFile]::OpenRead($env:AURORAVIEW_TEST_ARCHIVE); try { ConvertTo-Json -InputObject @($z.Entries | ForEach-Object { $_.FullName }) -Compress } finally { $z.Dispose() }'],
    { windowsHide: true, env: { ...process.env, AURORAVIEW_TEST_ARCHIVE: archive }, encoding: 'utf8' });
    const entries = JSON.parse(output).map(name => name.replaceAll('\\', '/'));
    assert.ok(entries.length > 0);
    assert.ok(entries.every(name => !name.includes('__pycache__') && !/\.py[co](\.meta)?$/.test(name)), entries.join('\n'));
    for (const file of files.filter(name => !name.startsWith('build/'))) {
      const packed = `com.auroraview.unity/${file}`;
      assert.ok(entries.includes(packed), `Missing ${packed}`);
      assert.equal(readFileSync(join(root, 'build/package', packed), 'utf8'), file);
    }
    assert.ok(entries.includes('com.auroraview.unity/WEBVIEW2-LICENSE.txt'));
    const digest = createHash('sha256').update(readFileSync(archive)).digest('hex');
    assert.equal(readFileSync(`${archive}.sha256`, 'utf8').trim(), `${digest}  ${basename(archive)}`);
  } finally {
    const temp = realpathSync(tmpdir());
    assert.ok(root.startsWith(`${temp}${sep}`) && basename(root).startsWith('auroraview-package-'));
    rmSync(root, { recursive: true, force: true });
  }
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

function importedFiles(root) {
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => {
    const path = join(root, entry.name);
    if (!entry.isDirectory()) return [path];
    // Unity's documented package rule excludes every folder ending in '~'.
    return entry.name.endsWith('~') ? [] : importedFiles(path);
  });
}

test('package staging stays outside Unity imports and retains clean release contents', { skip: process.platform !== 'win32' }, () => {
  const sourceRoot = fileURLToPath(new URL('..', import.meta.url));
  const plan = spawnSync('vx', ['--cache-mode', 'offline', '--no-auto-install', 'just', '--justfile', join(sourceRoot, 'justfile'), '--dry-run', 'build'], { windowsHide: true, encoding: 'utf8' });
  assert.equal(plan.status, 0, plan.stderr);
  const buildPath = /-B (\S+)/.exec(plan.stdout + plan.stderr)?.[1];
  assert.ok(buildPath, 'Native build must identify its CMake output directory');
  const nativeOutput = relative(sourceRoot, resolve(sourceRoot, buildPath));
  assert.ok(!nativeOutput.startsWith('..') && nativeOutput.split(sep).some(part => part.endsWith('~')), 'Native outputs inside the package must be hidden from Unity');
  const root = realpathSync(mkdtempSync(join(tmpdir(), 'auroraview-package-')));
  const files = [
    'package.json', 'Editor/SceneContracts.cs', 'Editor/AuroraView.Editor.asmdef',
    'Editor/Plugins/x86_64/auroraview_unity.dll', 'docs/core-runtime.md',
    'README.md', 'README.zh-CN.md', 'LICENSE', 'THIRD_PARTY.md',
    'Samples~/DccMcpSceneTools/Editor/DccMcpSceneTools.cs',
    'agent/core.py', 'agent/core.py.meta', 'agent/pipe-call.mjs', 'agent/pipe-call.mjs.meta',
    'agent/requirements-core.txt', 'agent/requirements-core.txt.meta',
    'agent/nested/helper.py', 'agent/nested/helper.py.meta',
    'build~/deps/webview2/LICENSE.txt',
  ];
  const caches = [
    'agent/__pycache__/core.cpython-311.pyc', 'agent/nested/__pycache__/helper.pyc',
    'agent/old.pyc', 'agent/nested/old.pyo', 'agent/__pycache__.meta',
    'agent/nested/__pycache__.meta', 'agent/old.pyc.meta', 'agent/nested/old.pyo.meta',
  ];
  const manifest = JSON.stringify({ name: 'com.auroraview.unity', version: '0.1.0', unity: '2022.3' });
  const content = file => file === 'package.json' ? manifest : file;
  try {
    for (const file of [...files, ...caches, join(nativeOutput, 'Release/auroraview_unity.dll')]) {
      const path = join(root, file);
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, content(file));
    }
    mkdirSync(join(root, 'scripts'));
    copyFileSync(fileURLToPath(new URL('../scripts/package.ps1', import.meta.url)), join(root, 'scripts/package.ps1'));
    execFileSync('powershell.exe', ['-NoProfile', '-File', join(root, 'scripts/package.ps1')], { windowsHide: true, stdio: 'pipe' });
    const outputDirectory = join(root, 'build~/package');
    assert.equal(existsSync(join(root, 'build')), false, 'Packaging must not create a Unity-visible build tree');
    for (const name of ['AuroraView.Editor.asmdef', 'auroraview_unity.dll']) {
      const visible = importedFiles(root).filter(path => basename(path) === name);
      assert.equal(visible.length, 1, `Unity must import only the source ${name}: ${visible.join(', ')}`);
    }
    const archives = readdirSync(outputDirectory).filter(name => name.endsWith('.zip'));
    assert.equal(archives.length, 1);
    const archive = join(outputDirectory, archives[0]);
    const output = execFileSync('powershell.exe', ['-NoProfile', '-Command',
      'Add-Type -AssemblyName System.IO.Compression.FileSystem; $z = [IO.Compression.ZipFile]::OpenRead($env:AURORAVIEW_TEST_ARCHIVE); try { ConvertTo-Json -InputObject @($z.Entries | ForEach-Object { $_.FullName }) -Compress } finally { $z.Dispose() }'],
    { windowsHide: true, env: { ...process.env, AURORAVIEW_TEST_ARCHIVE: archive }, encoding: 'utf8' });
    const entries = JSON.parse(output).map(name => name.replaceAll('\\', '/'));
    assert.ok(entries.length > 0);
    assert.ok(entries.every(name => !name.includes('__pycache__') && !/\.py[co](\.meta)?$/.test(name)), entries.join('\n'));
    for (const file of files.filter(name => !name.startsWith('build~/'))) {
      const packed = `com.auroraview.unity/${file}`;
      assert.ok(entries.includes(packed), `Missing ${packed}`);
      assert.equal(readFileSync(join(outputDirectory, packed), 'utf8'), content(file));
    }
    assert.equal(entries.filter(name => name.endsWith('/auroraview_unity.dll')).length, 1);
    assert.equal(entries.filter(name => name.endsWith('/AuroraView.Editor.asmdef')).length, 1);
    assert.equal(JSON.parse(readFileSync(join(outputDirectory, 'com.auroraview.unity/package.json'), 'utf8')).name, 'com.auroraview.unity');
    assert.ok(entries.includes('com.auroraview.unity/WEBVIEW2-LICENSE.txt'));
    const digest = createHash('sha256').update(readFileSync(archive)).digest('hex');
    assert.equal(readFileSync(`${archive}.sha256`, 'utf8').trim(), `${digest}  ${basename(archive)}`);
  } finally {
    const temp = realpathSync(tmpdir());
    assert.ok(root.startsWith(`${temp}${sep}`) && basename(root).startsWith('auroraview-package-'));
    rmSync(root, { recursive: true, force: true });
  }
});

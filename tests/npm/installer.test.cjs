'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const { createInstaller, sha256, downloadRelease } = require('../../launcher/installer.cjs');

function executable(subsystem = 2) {
  const bytes = Buffer.alloc(512);
  bytes.write('MZ'); bytes.writeUInt32LE(128, 60); bytes.write('PE\0\0', 128);
  bytes.writeUInt16LE(0x8664, 132); bytes.writeUInt16LE(subsystem, 128 + 92);
  return bytes;
}
const digest = bytes => crypto.createHash('sha256').update(bytes).digest('hex');

test('downloads reject unexpected hosts, credentials and plain HTTP before starting a process', () => {
  for (const url of ['http://github.com/release.zip', 'https://example.com/release.zip', 'https://user:password@github.com/release.zip']) {
    assert.throws(() => downloadRelease(url, 'unused', { run() { assert.fail('must not start'); } }), /pinned HTTPS GitHub/);
  }
});

test('the native downloader passes paths as arguments without a shell or disabling TLS checks', () => {
  const url = 'https://github.com/owner/repo/releases/download/v1/app.zip';
  const file = 'C:\\user files & symbols\\release.zip';
  downloadRelease(url, file, { command: 'C:\\Windows\\System32\\curl.exe', run(command, args, options) {
    assert.equal(command, 'C:\\Windows\\System32\\curl.exe');
    assert.equal(args[args.indexOf('--output') + 1], file);
    assert.equal(args.at(-1), url);
    assert.ok(!args.includes('--insecure')); assert.equal(options.shell, false);
    assert.equal(options.stdio, 'inherit'); assert.ok(options.timeout > 0);
    return { status: 0 };
  } });
});

for (const result of [{ status: 22 }, { status: 28 }, { status: null, error: { code: 'ETIMEDOUT' } },
  { status: null, error: { code: 'ENOENT' } }]) {
  test(`failed downloads report a browser fallback (${result.error?.code || result.status})`, () => {
    const url = 'https://github.com/owner/repo/releases/download/v1/app.zip';
    assert.throws(() => downloadRelease(url, 'unused', { run() { return result; } }), error => {
      assert.ok(error.message.includes(url));
      assert.match(error.message, /browser/);
      if (result.status === 28 || result.error?.code === 'ETIMEDOUT') assert.match(error.message, /timed out/);
      return true;
    });
  });
}

function fixture(t, overrides = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'autoresume npm & test-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const archive = Buffer.from('verified fixture archive');
  const metadata = structuredClone(require('../../launcher/release.json'));
  metadata.version = '0.1.0'; // Stable fixture, independent of the production release pin.
  metadata.app.sha256 = metadata.probe.sha256 = digest(archive);
  const calls = [];
  const installer = createInstaller({ root, platform: 'win32', arch: 'x64', metadata, log() {},
    async download(url, file) { calls.push(['download', url]); fs.writeFileSync(file, archive); },
    async extract(file, target) {
      calls.push(['extract', file]);
      assert.equal(sha256(file), digest(archive));
      fs.mkdirSync(target); fs.writeFileSync(path.join(target, 'CodexAutoResume.exe'), executable());
      fs.writeFileSync(path.join(target, 'CodexAutoResume.Probe.exe'), executable(3));
    }, ...overrides });
  return { root, installer, calls, metadata };
}

test('unsupported operating systems stop before touching disk or downloading', async t => {
  const { installer, root, calls } = fixture(t, { platform: 'linux' });
  await assert.rejects(installer.install(), /Windows x64 only/);
  assert.deepEqual(calls, []); assert.deepEqual(fs.readdirSync(root), []);
});

test('unsupported architecture stops before downloading', async t => {
  const { installer, calls } = fixture(t, { arch: 'arm64' });
  await assert.rejects(installer.install(), /Windows x64 only/); assert.deepEqual(calls, []);
});

test('verified archive installs atomically and the cached executable is reused', async t => {
  const { installer, root, calls } = fixture(t);
  const file = await installer.install();
  assert.equal(file, path.join(root, 'app-0.1.0', 'CodexAutoResume.exe'));
  assert.equal(await installer.install(), file);
  assert.equal(calls.filter(call => call[0] === 'download').length, 1);
  assert.deepEqual(fs.readdirSync(root), ['app-0.1.0']);
});

test('checksum mismatch prevents extraction and cleans temporary files', async t => {
  const { installer, root, calls } = fixture(t, { async download(url, file) { fs.writeFileSync(file, 'tampered'); } });
  await assert.rejects(installer.install(), /SHA-256 mismatch/);
  assert.ok(!calls.some(call => call[0] === 'extract'));
  assert.deepEqual(fs.readdirSync(root), []);
});

test('network errors leave no partially installed application', async t => {
  const { installer, root } = fixture(t, { async download() { throw new Error('Network unavailable'); } });
  await assert.rejects(installer.install(), /Network unavailable/); assert.deepEqual(fs.readdirSync(root), []);
});

test('an invalid Windows executable cannot become the installed application', async t => {
  const { installer, root } = fixture(t, { async extract(file, target) {
    fs.mkdirSync(target); fs.writeFileSync(path.join(target, 'CodexAutoResume.exe'), 'not an exe');
  } });
  await assert.rejects(installer.install(), /valid Windows executable/); assert.deepEqual(fs.readdirSync(root), []);
});

test('wrong executable architecture or subsystem is rejected', async t => {
  const { installer } = fixture(t, { async extract(file, target) {
    const bytes = executable(); bytes.writeUInt16LE(0xAA64, 132);
    fs.mkdirSync(target); fs.writeFileSync(path.join(target, 'CodexAutoResume.exe'), bytes);
  } });
  await assert.rejects(installer.install(), /expected Windows x64/);
});

test('a changed cached executable is not launched or silently replaced', async t => {
  const { installer, calls } = fixture(t);
  const file = await installer.install(); fs.appendFileSync(file, 'changed');
  assert.throws(() => installer.getInstalled(), /incomplete or changed/);
  await assert.rejects(installer.install(), /reinstall/);
  assert.equal(calls.filter(call => call[0] === 'download').length, 1);
});

test('application and diagnostic installs maintain separate files', async t => {
  const { installer, root } = fixture(t);
  const app = await installer.install(); const probe = await installer.install('probe');
  assert.notEqual(path.dirname(app), path.dirname(probe));
  assert.equal(installer.getInstalled(), app); assert.equal(installer.getInstalled('probe'), probe);
  assert.deepEqual(fs.readdirSync(root).sort(), ['app-0.1.0', 'probe-0.1.0']);
});

test('concurrent installs reuse completed files without removing one another', async t => {
  const { installer, root } = fixture(t);
  const [a, b] = await Promise.all([installer.install(), installer.install()]);
  assert.equal(a, b); assert.deepEqual(fs.readdirSync(root), ['app-0.1.0']);
});

test('failed installs preserve files outside their temporary directory', async t => {
  const { installer, root } = fixture(t, { async download() { throw new Error('offline'); } });
  const settings = path.join(root, 'settings-to-preserve.json'); fs.writeFileSync(settings, 'user data');
  await assert.rejects(installer.install()); assert.equal(fs.readFileSync(settings, 'utf8'), 'user data');
});

for (const mode of ['traversal', 'symlink']) {
  test(`native ZIP extraction rejects ${mode} entries`, { skip: process.platform !== 'win32' }, async t => {
    const { root, metadata } = fixture(t);
    const archive = path.join(root, 'unsafe.zip');
    const script = `$ErrorActionPreference = 'Stop'
      Add-Type -AssemblyName System.IO.Compression
      Add-Type -AssemblyName System.IO.Compression.FileSystem
      $zip = [IO.Compression.ZipFile]::Open($env:AUTORESUME_TEST_ARCHIVE, [IO.Compression.ZipArchiveMode]::Create)
      $name = if ($env:AUTORESUME_TEST_MODE -eq 'traversal') { '../outside.txt' } else { 'link.txt' }
      $entry = $zip.CreateEntry($name)
      if ($env:AUTORESUME_TEST_MODE -eq 'symlink') { $entry.ExternalAttributes = -1610612736 }
      $writer = New-Object IO.StreamWriter($entry.Open()); $writer.Write('unsafe'); $writer.Dispose(); $zip.Dispose()`;
    const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], {
      env: { ...process.env, AUTORESUME_TEST_ARCHIVE: archive, AUTORESUME_TEST_MODE: mode }, encoding: 'utf8',
    });
    assert.equal(result.status, 0, result.stderr);
    metadata.app.sha256 = sha256(archive);
    const installRoot = path.join(root, 'native');
    const installer = createInstaller({ root: installRoot, platform: 'win32', arch: 'x64', metadata, log() {},
      download(url, file) { fs.copyFileSync(archive, file); } });
    await assert.rejects(installer.install(), /Could not extract/);
    assert.ok(!fs.existsSync(path.join(installRoot, 'outside.txt')));
    assert.deepEqual(fs.readdirSync(installRoot), []);
  });
}

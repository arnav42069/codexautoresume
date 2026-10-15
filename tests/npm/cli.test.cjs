'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { run } = require('../../npm/cli.cjs');

function fixture() {
  const calls = [];
  const installer = {
    version: '0.1.0', checkPlatform() { calls.push(['platform']); },
    getInstalled(kind) { calls.push(['installed', kind]); return 'C:\\with spaces & symbols\\app.exe'; },
    async install(kind) { calls.push(['install', kind]); return 'C:\\with spaces & symbols\\probe.exe'; },
  };
  return { calls, installer, log() {} };
}

test('help and version do not download or execute anything', async () => {
  const f = fixture();
  assert.equal(await run(['--help'], f), 0); assert.equal(await run(['--version'], f), 0);
  assert.deepEqual(f.calls, []);
});

test('launch detaches the exact installed executable without a shell', async () => {
  const f = fixture(); let released = false;
  const code = await run([], { ...f, spawn(file, args, options) {
    assert.equal(file, 'C:\\with spaces & symbols\\app.exe'); assert.deepEqual(args, []);
    assert.equal(options.shell, false); assert.equal(options.detached, true);
    const child = new EventEmitter(); child.unref = () => { released = true; };
    process.nextTick(() => child.emit('spawn')); return child;
  } });
  assert.equal(code, 0); assert.ok(released); assert.ok(!f.calls.some(call => call[0] === 'install'));
});

test('self-test waits for the native process and returns its exit code', async () => {
  const f = fixture();
  assert.equal(await run(['--self-test'], { ...f, spawnSync(file, args, options) {
    assert.deepEqual(args, ['--self-test']); assert.equal(options.shell, false); return { status: 7 };
  } }), 7);
});

test('verification forwards the original arguments without asserting ownership for the user', async () => {
  const f = fixture(); const args = ['UUID', '--codex', 'C:\\Codex & tools\\codex.exe'];
  assert.equal(await run(['verify', ...args], { ...f, spawnSync(file, received, options) {
    assert.deepEqual(received, args); assert.equal(options.shell, false); return { status: 2 };
  } }), 2);
  assert.ok(f.calls.some(call => call[0] === 'install' && call[1] === 'probe'));
});

test('verification without arguments does not download anything', async () => {
  const f = fixture(); assert.equal(await run(['verify'], f), 2);
  assert.ok(!f.calls.some(call => call[0] === 'install'));
});

test('lifecycle repair and optional diagnostic installation select the correct component', async () => {
  const f = fixture();
  assert.equal(await run(['install'], f), 0); assert.equal(await run(['install', '--probe'], f), 0);
  assert.deepEqual(f.calls.filter(call => call[0] === 'install'), [['install', 'app'], ['install', 'probe']]);
});

test('disabled lifecycle scripts require an explicit install command', async () => {
  const f = fixture(); f.installer.getInstalled = () => null;
  await assert.rejects(run([], f), /codexautoresume install/);
  assert.ok(!f.calls.some(call => call[0] === 'install'));
});

test('unknown commands and invalid install arguments do not execute processes', async () => {
  const f = fixture(); await assert.rejects(run(['unexpected'], f), /Unknown command/);
  await assert.rejects(run(['install', '--unknown'], f), /install/);
  assert.ok(!f.calls.some(call => call[0] === 'install'));
});

test('native process errors are reported instead of claiming a launch succeeded', async () => {
  const f = fixture();
  await assert.rejects(run([], { ...f, spawn() {
    const child = new EventEmitter(); process.nextTick(() => child.emit('error', new Error('cannot launch'))); return child;
  } }), /cannot launch/);
});

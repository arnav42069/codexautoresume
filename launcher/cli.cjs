#!/usr/bin/env node
'use strict';

const childProcess = require('node:child_process');
const { createInstaller } = require('./installer.cjs');

const help = `Codex AutoResume — Windows tray app

  codexautoresume                   Open the app
  codexautoresume verify <args>     Run the Desktop continuity diagnostic
  codexautoresume install           Install if npm lifecycle scripts were disabled
  codexautoresume install --probe   Download the optional diagnostic
  codexautoresume --version         Show the pinned application release

Before using Desktop sessions, verify their original UUIDs with Desktop closed:
  codexautoresume verify UUID --desktop-closed --codex "C:\\path\\codex.exe"
`;

async function run(args, { installer = createInstaller(), spawn = childProcess.spawn,
  spawnSync = childProcess.spawnSync, log = console.log } = {}) {
  if (args[0] === '--help' || args[0] === '-h') { log(help); return 0; }
  if (args[0] === '--version') { log(installer.version); return 0; }
  installer.checkPlatform();
  if (args[0] === 'install') {
    if (args.length > 2 || (args[1] && args[1] !== '--probe')) throw new Error('Use codexautoresume install [--probe].');
    await installer.install(args[1] === '--probe' ? 'probe' : 'app');
    log('Installed.'); return 0;
  }
  if (args[0] === 'verify') {
    if (args.length < 2) { log(help); return 2; }
    const executable = await installer.install('probe');
    const result = spawnSync(executable, args.slice(1), { stdio: 'inherit', shell: false });
    if (result.error) throw result.error;
    return result.status ?? 1;
  }
  if (args.length && args[0] !== '--self-test') throw new Error('Unknown command. Use codexautoresume --help.');
  const executable = installer.getInstalled('app');
  if (!executable) throw new Error('The app is not installed. Run codexautoresume install.');
  if (args[0] === '--self-test') {
    const result = spawnSync(executable, ['--self-test'], { stdio: 'inherit', shell: false });
    if (result.error) throw result.error;
    return result.status ?? 1;
  }
  const child = spawn(executable, [], { detached: true, stdio: 'ignore', shell: false });
  await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
  child.unref();
  return 0;
}

if (require.main === module) {
  run(process.argv.slice(2)).then(code => { process.exitCode = code; }).catch(error => {
    console.error(error.message); process.exitCode = 1;
  });
}
module.exports = { run };

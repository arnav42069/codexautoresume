'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const release = require('./release.json');

function downloadRelease(url, file, { run = spawnSync,
  command = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'curl.exe') } = {}) {
  const source = new URL(url);
  if (source.protocol !== 'https:' || source.hostname !== 'github.com' || source.username || source.password) {
    throw new Error('Only the pinned HTTPS GitHub release is supported.');
  }
  // Windows ships curl. Its transfer meter stays visible with npm --foreground-scripts,
  // and avoids Windows PowerShell's slow Invoke-WebRequest download path.
  const result = run(command, ['--fail', '--location', '--show-error',
    '--proto', '=https', '--proto-redir', '=https', '--max-redirs', '5',
    '--connect-timeout', '20', '--max-time', '300', '--speed-limit', '1024', '--speed-time', '45',
    '--retry', '1', '--retry-delay', '2', '--retry-max-time', '10',
    '--output', file, url], { stdio: 'inherit', windowsHide: true, shell: false, timeout: 310000 });
  if (result.error?.code === 'ENOENT') {
    throw new Error('Windows curl.exe is unavailable. Download and extract the ZIP in your browser: ' + url);
  }
  if (result.error || result.status !== 0) {
    const reason = result.status === 28 || result.error?.code === 'ETIMEDOUT'
      ? 'The download stalled or timed out.' : `The HTTPS download failed (curl ${result.status ?? 'terminated'}).`;
    throw new Error(reason + ' You can download and extract the ZIP in your browser: ' + url);
  }
}

function sha256(file) {
  const hash = crypto.createHash('sha256');
  const descriptor = fs.openSync(file, 'r');
  try {
    const buffer = Buffer.alloc(1024 * 1024);
    let length;
    while ((length = fs.readSync(descriptor, buffer)) > 0) hash.update(buffer.subarray(0, length));
    return hash.digest('hex');
  } finally { fs.closeSync(descriptor); }
}

function assertExecutable(file, subsystem) {
  const descriptor = fs.openSync(file, 'r');
  try {
    const dos = Buffer.alloc(64);
    if (fs.readSync(descriptor, dos, 0, dos.length, 0) !== dos.length || dos.toString('ascii', 0, 2) !== 'MZ') {
      throw new Error('The release does not contain a valid Windows executable.');
    }
    const header = Buffer.alloc(94);
    const offset = dos.readUInt32LE(60);
    if (fs.readSync(descriptor, header, 0, header.length, offset) !== header.length ||
        header.toString('hex', 0, 4) !== '50450000' || header.readUInt16LE(4) !== 0x8664 ||
        header.readUInt16LE(92) !== subsystem) {
      throw new Error('The release executable is not the expected Windows x64 application.');
    }
  } finally { fs.closeSync(descriptor); }
}

function powershell(action, source, destination) {
  // Pass paths via the environment; never interpolate user paths into PowerShell code.
  const script = fs.readFileSync(path.join(__dirname, 'windows.ps1'), 'utf8');
  const result = spawnSync('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', script], {
    stdio: 'inherit', windowsHide: true, shell: false, timeout: 120000,
    env: { ...process.env, AUTORESUME_INSTALL_ACTION: action,
      AUTORESUME_INSTALL_SOURCE: source, AUTORESUME_INSTALL_DESTINATION: destination },
  });
  if (result.error || result.status !== 0) {
    throw new Error('Could not ' + action + ' the Windows release. Check the connection and PowerShell availability, then reinstall.');
  }
}

function createInstaller({ root = path.resolve(__dirname, '..', 'native'), platform = process.platform,
  arch = process.arch, metadata = release,
  download = downloadRelease,
  extract = (archive, directory) => powershell('extract', archive, directory),
  log = console.log } = {}) {
  function checkPlatform() {
    if (platform !== 'win32' || arch !== 'x64') throw new Error('Codex AutoResume currently supports Windows x64 only.');
  }
  function component(kind) {
    if (kind !== 'app' && kind !== 'probe') throw new Error('Unknown application component.');
    return metadata[kind];
  }
  function directory(kind) { return path.join(root, `${kind}-${metadata.version}`); }
  function getInstalled(kind = 'app') {
    checkPlatform();
    const info = component(kind);
    const target = directory(kind);
    if (!fs.existsSync(target)) return null;
    try {
      const manifest = JSON.parse(fs.readFileSync(path.join(target, 'install.json'), 'utf8'));
      const executable = path.join(target, info.executable);
      if (manifest.version !== metadata.version || manifest.archiveSha256 !== info.sha256 ||
          !/^[a-f0-9]{64}$/.test(manifest.executableSha256) || sha256(executable) !== manifest.executableSha256) {
        throw new Error('Installation integrity check failed.');
      }
      assertExecutable(executable, info.subsystem);
      return executable;
    } catch {
      throw new Error('The installed files are incomplete or changed. Close AutoResume from the tray and reinstall the npm package. Saved settings are separate.');
    }
  }
  async function install(kind = 'app') {
    checkPlatform();
    const info = component(kind);
    const existing = getInstalled(kind);
    if (existing) return existing;
    fs.mkdirSync(root, { recursive: true });
    const staging = fs.mkdtempSync(path.join(root, '.install-'));
    try {
      const archive = path.join(staging, 'release.zip');
      const unpacked = path.join(staging, 'unpacked');
      log(`Downloading Codex AutoResume ${kind === 'probe' ? 'diagnostic ' : ''}v${metadata.version}…`);
      log(metadata.baseUrl + info.asset);
      await download(metadata.baseUrl + info.asset, archive);
      log('Checking SHA-256…');
      if (sha256(archive) !== info.sha256) throw new Error('Release SHA-256 mismatch. Installation stopped; nothing was extracted.');
      log('Extracting the Windows app…');
      await extract(archive, unpacked);
      const executable = path.join(unpacked, info.executable);
      assertExecutable(executable, info.subsystem);
      fs.writeFileSync(path.join(unpacked, 'install.json'), JSON.stringify({ version: metadata.version,
        archiveSha256: info.sha256, executableSha256: sha256(executable) }, null, 2) + '\n', { flag: 'wx' });
      // A concurrent installer may have completed while this download was in progress.
      const concurrent = getInstalled(kind);
      if (concurrent) return concurrent;
      fs.renameSync(unpacked, directory(kind));
      return getInstalled(kind);
    } finally { fs.rmSync(staging, { recursive: true, force: true }); }
  }
  return { install, getInstalled, checkPlatform, version: metadata.version };
}

module.exports = { createInstaller, assertExecutable, sha256, downloadRelease };

'use strict';

const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');
const release = require('./release.json');
const { unzipSync } = require('fflate');

function downloadRelease(url, file, { run = spawnSync,
  command = process.platform === 'win32' ? path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'curl.exe') : 'curl' } = {}) {
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
    throw new Error('curl is unavailable. Download and extract the ZIP in your browser: ' + url);
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

function assertExecutable(file, subsystem, platform = 'win32', arch = 'x64') {
  const descriptor = fs.openSync(file, 'r');
  try {
    const dos = Buffer.alloc(64);
    if (platform !== 'win32') {
      if (fs.readSync(descriptor, dos, 0, dos.length, 0) !== dos.length) throw new Error('Invalid native executable.');
      if (platform === 'linux') {
        if (dos.toString('hex', 0, 4) !== '7f454c46' || dos[4] !== 2 || dos[5] !== 1 ||
            dos.readUInt16LE(18) !== (arch === 'arm64' ? 183 : 62)) throw new Error('Unexpected Linux executable architecture.');
      } else if (platform === 'darwin') {
        if (dos.toString('hex', 0, 4) !== 'cffaedfe' || dos.readUInt32LE(4) !== (arch === 'arm64' ? 0x100000c : 0x1000007))
          throw new Error('Unexpected macOS executable architecture.');
      } else throw new Error('Unsupported executable format.');
      return;
    }
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

function extractPortable(archive, directory) {
  let total = 0;
  const files = unzipSync(fs.readFileSync(archive), { filter(entry) {
    const parts = entry.name.split('/');
    if (!entry.name || entry.name.startsWith('/') || /[\\:\x00-\x1f]/.test(entry.name) ||
        parts.some(part => ['..', '__proto__', 'constructor', 'prototype'].includes(part))) throw new Error('Unsafe ZIP path.');
    total += entry.originalSize;
    if (entry.originalSize > 512 * 1024 * 1024 || total > 1024 * 1024 * 1024) throw new Error('Release ZIP is too large.');
    return true;
  } });
  for (const [name, bytes] of Object.entries(files)) {
    const target = path.resolve(directory, name);
    if (!target.startsWith(path.resolve(directory) + path.sep)) throw new Error('ZIP path escapes the installation directory.');
    // Write regular files only. Archive symlink attributes never create filesystem links.
    if (name.endsWith('/')) fs.mkdirSync(target, { recursive: true });
    else { fs.mkdirSync(path.dirname(target), { recursive: true }); fs.writeFileSync(target, bytes, { flag: 'wx' }); }
  }
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
  extract = (archive, directory) => platform === 'win32' ? powershell('extract', archive, directory) : extractPortable(archive, directory),
  runShortcut = (executable, force) => spawnSync(executable, [force ? '--create-shortcut' : '--ensure-shortcut'],
    { stdio: 'inherit', shell: false, windowsHide: true, timeout: 20000 }),
  log = console.log } = {}) {
  function checkPlatform() {
    if (!['win32', 'linux', 'darwin'].includes(platform) || !['x64', 'arm64'].includes(arch)) throw new Error('Unsupported platform or architecture.');
    if (!metadata.platforms?.[`${platform}-${arch}`] && !(platform === 'win32' && arch === 'x64' && metadata.app))
      throw new Error(`No release is available for ${platform}-${arch}. Check GitHub Releases.`);
  }
  function component(kind) {
    if (kind !== 'app' && kind !== 'probe') throw new Error('Unknown application component.');
    const info = (metadata.platforms?.[`${platform}-${arch}`] || metadata)[kind];
    if (!info) throw new Error('The diagnostic is not available for this platform.');
    return info;
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
      assertExecutable(executable, info.subsystem, platform, arch);
      return executable;
    } catch {
      throw new Error('The installed files are incomplete or changed. Close AutoResume from the tray and reinstall the npm package. Saved settings are separate.');
    }
  }
  function createShortcut(force = false) {
    const executable = getInstalled('app');
    if (!executable) throw new Error('Install the application first.');
    if (!component('app').desktopShortcut) return false;
    const result = runShortcut(executable, force);
    if (result.error || result.status !== 0) throw new Error('Could not create the desktop shortcut. Run codexautoresume shortcut to retry.');
    return true;
  }
  function finish(executable, kind) {
    if (kind === 'app' && component('app').desktopShortcut) {
      try { createShortcut(); } catch (error) { log('App installed. ' + error.message); }
    }
    return executable;
  }
  async function install(kind = 'app') {
    checkPlatform();
    const info = component(kind);
    const existing = getInstalled(kind);
    if (existing) return finish(existing, kind);
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
      log('Extracting the app…');
      await extract(archive, unpacked);
      const executable = path.join(unpacked, info.executable);
      assertExecutable(executable, info.subsystem, platform, arch);
      if (platform !== 'win32') fs.chmodSync(executable, 0o755);
      fs.writeFileSync(path.join(unpacked, 'install.json'), JSON.stringify({ version: metadata.version,
        archiveSha256: info.sha256, executableSha256: sha256(executable) }, null, 2) + '\n', { flag: 'wx' });
      // A concurrent installer may have completed while this download was in progress.
      const concurrent = getInstalled(kind);
      if (concurrent) return finish(concurrent, kind);
      fs.renameSync(unpacked, directory(kind));
      return finish(getInstalled(kind), kind);
    } finally { fs.rmSync(staging, { recursive: true, force: true }); }
  }
  return { install, getInstalled, checkPlatform, createShortcut, version: metadata.version };
}

module.exports = { createInstaller, assertExecutable, sha256, downloadRelease, extractPortable };

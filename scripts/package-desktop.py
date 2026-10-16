#!/usr/bin/env python3
"""Package an already-published desktop build; never fetch or build dependencies."""
import pathlib, plistlib, shutil, subprocess, sys, tempfile, zipfile

rid = sys.argv[1]
root = pathlib.Path(__file__).resolve().parent.parent
artifacts = root / 'artifacts'
published = artifacts / rid
staging = artifacts / ('package-' + rid)
if staging.exists(): shutil.rmtree(staging)
staging.mkdir(parents=True)
if rid.startswith('osx-'):
    bundle = staging / 'CodexAutoResume.app'
    contents = bundle / 'Contents'
    binary = contents / 'MacOS'
    shutil.copytree(published, binary)
    resources = contents / 'Resources'; resources.mkdir()
    with (contents / 'Info.plist').open('wb') as f:
        plistlib.dump({'CFBundleName': 'Codex AutoResume', 'CFBundleDisplayName': 'Codex AutoResume',
            'CFBundleIdentifier': 'dev.arnav.codexautoresume', 'CFBundleExecutable': 'CodexAutoResume',
            'CFBundlePackageType': 'APPL', 'CFBundleVersion': '0.3.0', 'CFBundleShortVersionString': '0.3.0',
            'CFBundleIconFile': 'icon.icns', 'NSHighResolutionCapable': True, 'LSMinimumSystemVersion': '12.0',
            'NSDesktopFolderUsageDescription': 'Create your Codex AutoResume desktop shortcut.'}, f)
    with tempfile.TemporaryDirectory() as work:
        iconset = pathlib.Path(work) / 'icon.iconset'; iconset.mkdir()
        for size in (16,32,128,256,512):
            for scale in (1,2):
                name = f'icon_{size}x{size}' + ('@2x' if scale == 2 else '') + '.png'
                subprocess.run(['sips','-z',str(size*scale),str(size*scale),str(root/'assets/icon.png'),'--out',str(iconset/name)],check=True,stdout=subprocess.DEVNULL)
        subprocess.run(['iconutil','-c','icns',str(iconset),'-o',str(resources/'icon.icns')],check=True)
    subprocess.run(['codesign','--force','--deep','--sign','-',str(bundle)],check=True)
    subprocess.run(['codesign','--verify','--deep','--strict',str(bundle)],check=True)
    shortcut = staging / 'Create desktop shortcut.command'
    shortcut.write_text('#!/bin/sh\nset -eu\ncd -- "$(dirname -- "$0")"\nexec ./CodexAutoResume.app/Contents/MacOS/CodexAutoResume --create-shortcut\n')
else:
    shutil.copytree(published, staging, dirs_exist_ok=True)
    shortcut = staging / 'create-shortcut.sh'
    shortcut.write_text('#!/bin/sh\nset -eu\ncd -- "$(dirname -- "$0")"\nexec ./CodexAutoResume --create-shortcut\n')
shortcut.chmod(0o755)
shutil.copy(root/'README.md',staging/'README.md'); shutil.copy(root/'LICENSE',staging/'LICENSE')
shutil.copytree(root/'docs',staging/'docs')
def archive(source, destination):
    with zipfile.ZipFile(destination,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as output:
        for file in sorted(source.rglob('*')):
            if file.is_file(): output.write(file,file.relative_to(source))
archive(staging, artifacts / f'CodexAutoResume-{rid}.zip')
probe = artifacts / ('probe-' + rid)
if rid.startswith('osx-'):
    subprocess.run(['codesign','--force','--sign','-',str(probe/'CodexAutoResume.Probe')],check=True)
shutil.copy(root/'docs/DESKTOP-FEASIBILITY.md',probe/'README.md'); shutil.copy(root/'LICENSE',probe/'LICENSE')
archive(probe, artifacts / f'CodexAutoResume-DesktopProbe-{rid}.zip')
print('Packaged desktop and diagnostic:',rid)

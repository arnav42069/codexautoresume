#!/usr/bin/env python3
import hashlib,json,pathlib,sys
tag=sys.argv[1]; directory=pathlib.Path('artifacts')
manifest={'version':tag.removeprefix('v'),'baseUrl':f'https://github.com/arnav42069/codexautoresume/releases/download/{tag}/','platforms':{}}
checks=[]
for platform,rid in [('win32-x64','win-x64'),('linux-x64','linux-x64'),('linux-arm64','linux-arm64'),('darwin-x64','osx-x64'),('darwin-arm64','osx-arm64')]:
    components={}
    for kind in ['app','probe']:
        name=f'CodexAutoResume-{rid}.zip' if kind=='app' else f'CodexAutoResume-DesktopProbe-{rid}.zip'
        file=directory/name
        if not file.exists():raise SystemExit('Missing release asset: '+name)
        digest=hashlib.sha256(file.read_bytes()).hexdigest();checks.append(f'{digest}  {name}')
        executable='CodexAutoResume' if kind=='app' else 'CodexAutoResume.Probe'
        if rid.startswith('win-'):executable+='.exe'
        if rid.startswith('osx-') and kind=='app':executable='CodexAutoResume.app/Contents/MacOS/CodexAutoResume'
        components[kind]={'asset':name,'sha256':digest,'executable':executable}
        if rid.startswith('win-'):components[kind]['subsystem']=2 if kind=='app' else 3
        if kind=='app':components[kind]['desktopShortcut']=True
    manifest['platforms'][platform]=components
(directory/'SHA256SUMS.txt').write_text('\n'.join(checks)+'\n')
(directory/'npm-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')

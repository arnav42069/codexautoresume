#!/usr/bin/env python3
import hashlib,json,pathlib,subprocess,sys,tempfile,zipfile
tag=sys.argv[1]; manifest=json.loads(pathlib.Path('artifacts/npm-manifest.json').read_text())
base=manifest['baseUrl']
subprocess.run(['curl','-fsSL','--retry','2',f'https://github.com/arnav42069/codexautoresume/releases/tag/{tag}','-o','/dev/null'],check=True)
with tempfile.TemporaryDirectory() as work:
    for platform,components in manifest['platforms'].items():
        for info in components.values():
            file=pathlib.Path(work)/info['asset']
            subprocess.run(['curl','-fsSL','--retry','2','--connect-timeout','20','--max-time','180',base+info['asset'],'-o',str(file)],check=True)
            assert hashlib.sha256(file.read_bytes()).hexdigest()==info['sha256'], 'Downloaded checksum mismatch'
            with zipfile.ZipFile(file) as archive:
                assert info['executable'] in archive.namelist(), 'Executable missing from release'
                assert archive.testzip() is None, 'Damaged ZIP'
            print('Verified public download:',platform,info['asset'])

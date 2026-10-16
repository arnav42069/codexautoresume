#!/usr/bin/env python3
import os,pathlib,subprocess,sys,tempfile,shutil
rid=sys.argv[1]
root=pathlib.Path(__file__).resolve().parent.parent
folder=root/'artifacts'/('package-'+rid)
exe=folder/'CodexAutoResume.app/Contents/MacOS/CodexAutoResume' if rid.startswith('osx-') else folder/'CodexAutoResume'
with tempfile.TemporaryDirectory(prefix='autoresume native test ') as work:
    env={**os.environ,'AUTORESUME_DATA_HOME':work+'/state','AUTORESUME_DESKTOP_DIR':work+'/Desktop','XDG_DATA_HOME':work+'/xdg'}
    command=([str(exe)] if rid.startswith('osx-') or env.get('DISPLAY') else ['xvfb-run','-a',str(exe)])
    fixture=pathlib.Path(work)/'Codex fixture'; fixture.mkdir()
    test_bin=root/'tests/CodexAutoResume.Tests/bin/Release/net8.0'
    for file in test_bin.iterdir():
        if file.is_file():shutil.copy2(file,fixture/file.name)
    shutil.copy2(test_bin/'CodexAutoResume.Tests',fixture/'codex')
    (fixture/'codex').chmod(0o755)
    subprocess.run(command+['--self-test','--fixture-directory',str(fixture),'--screenshot',work+'/window.png'],check=True,env=env,timeout=60)
    assert pathlib.Path(work+'/window.png').stat().st_size>1000
    subprocess.run([str(exe),'--create-shortcut'],check=True,env=env,timeout=30)
    shortcut=pathlib.Path(work+'/Desktop')/('Codex AutoResume.app' if rid.startswith('osx-') else 'Codex AutoResume.desktop')
    assert shortcut.exists(), 'Desktop shortcut was not created'
    if rid.startswith('osx-'):assert shortcut.is_symlink() and shortcut.resolve()==exe.parents[2]
    else:assert str(exe) in shortcut.read_text() and 'Terminal=false' in shortcut.read_text()
    print('PASS packaged UI rendering and native desktop shortcut:',rid)

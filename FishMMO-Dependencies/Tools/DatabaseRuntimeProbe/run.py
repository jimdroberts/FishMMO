#!/usr/bin/env python3
"""Read-only EF/Npgsql checks using the actual Unity Mono and AOT framework profiles."""
import argparse,json,os,re,shutil,subprocess,tempfile
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--editor',required=True,type=Path,help='Path to the Unity Editor executable')
parser.add_argument('--settings',type=Path)
parser.add_argument('--driver',type=Path,help='Patched Npgsql DLL; defaults to Unity Assets/Dependencies/Npgsql.dll')
parser.add_argument('--artifacts',type=Path)
parser.add_argument('--native',action='store_true',help='Also build/run isolated Linux Mono and IL2CPP players')
args=parser.parse_args()
source=Path(__file__).resolve().parent
repo=source.parents[2]
deps=repo/'FishMMO-Unity/Assets/Dependencies'
settings=args.settings or repo/'Builds/Server/Linux Server/FishMMO GameServer Linux/appsettings.json'
patched=args.driver or deps/'Npgsql.dll'
original=Path(os.environ.get('NUGET_PACKAGES',str(Path.home()/'.nuget/packages')))/'npgsql/5.0.18/lib/netstandard2.1/Npgsql.dll'
artifacts=args.artifacts or Path(tempfile.mkdtemp(prefix='fishmmo-db-runtime-'))
artifacts.mkdir(parents=True,exist_ok=True)
editor=args.editor.resolve()
runtime=editor.parent/'Data/MonoBleedingEdge'
mono=runtime/'bin/mono'
if not mono.exists():mono=runtime/'bin/mono.exe'
for path in [editor,mono,settings,patched,original]:
    if not path.exists():raise SystemExit('Required file missing: '+str(path))
print('Artifacts:',artifacts,flush=True)
sdks=subprocess.check_output(['dotnet','--list-sdks'],text=True).splitlines()
version,base=re.fullmatch(r'(\S+)\s+\[(.*)\]',sdks[-1]).groups()
csc=Path(base)/version/'Roslyn/bincore/csc.dll'
aot=runtime/'lib/mono/unityaot-linux'
refs=[aot/'mscorlib.dll',aot/'System.dll',aot/'System.Core.dll',aot/'System.Data.dll',aot/'Facades/netstandard.dll']+list(deps.glob('*.dll'))
compile_args=['dotnet',str(csc),'/nologo','/nostdlib','/target:exe','/out:'+str(artifacts/'Probe.exe')]+['/reference:'+str(p) for p in refs]+[str(source/'Probe.cs')]
subprocess.run(compile_args,check=True)
summary={}
for name,profile,driver,expected in [('OriginalMono','unityjit-linux',original,0),('OriginalAot','unityaot-linux',original,1),('PatchedMono','unityjit-linux',patched,0),('PatchedAot','unityaot-linux',patched,0)]:
    folder=artifacts/name;folder.mkdir(exist_ok=True)
    for dll in deps.glob('*.dll'):shutil.copy2(dll,folder/dll.name)
    shutil.copy2(artifacts/'Probe.exe',folder/'Probe.exe')
    shutil.copy2(driver,folder/'Npgsql.dll')
    framework=runtime/'lib/mono'/profile
    for dll in ['System.Core.dll','System.dll','System.Xml.dll','System.Configuration.dll']:
        shutil.copy2(framework/dll,folder/dll)
    with (artifacts/(name+'.log')).open('w') as log:
        result=subprocess.run([str(mono),str(folder/'Probe.exe'),str(settings)],stdout=log,stderr=subprocess.STDOUT,timeout=120)
    output=(artifacts/(name+'.log')).read_text(errors='replace')
    if expected:
        passed=result.returncode!=0 and 'NotImplemented: byref delegate' in output and 'GenerateNonGenericValidationMethod' in output
    else:passed=result.returncode==0 and 'PASS: read-only EF/driver compatibility probe.' in output
    summary[name]={'exitCode':result.returncode,'passed':passed}
    print(name,summary[name],flush=True)
    if not passed:
        (artifacts/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
        raise SystemExit('Unexpected result; inspect '+str(artifacts/(name+'.log')))
if args.native:
    project=artifacts/'NativeProject'
    (project/'Assets/Editor').mkdir(parents=True,exist_ok=True)
    (project/'Assets/Dependencies').mkdir(exist_ok=True)
    (project/'Packages').mkdir(exist_ok=True)
    (project/'ProjectSettings').mkdir(exist_ok=True)
    for dll in deps.glob('*.dll'):shutil.copy2(dll,project/'Assets/Dependencies'/dll.name)
    shutil.copy2(patched,project/'Assets/Dependencies/Npgsql.dll')
    shutil.copy2(source/'Probe.cs',project/'Assets/Probe.cs')
    shutil.copy2(source/'Native/ProbeBootstrap.cs',project/'Assets/ProbeBootstrap.cs')
    shutil.copy2(source/'Native/BuildProbe.cs',project/'Assets/Editor/BuildProbe.cs')
    shutil.copy2(source/'Native/link.xml',project/'Assets/link.xml')
    shutil.copy2(repo/'FishMMO-Unity/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
    (project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    subprocess.run([str(editor),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','BuildProbe.Build','-logFile',str(artifacts/'native-build.log')],check=True)
    for name in ['Mono2x','IL2CPP']:
        with (artifacts/(name+'-stdout.log')).open('w') as log:
            result=subprocess.run([str(project/'Builds'/name/'Probe.x86_64'),'-batchmode','-nographics','--db-settings',str(settings),'-logFile',str(artifacts/(name+'-player.log'))],stdout=log,stderr=subprocess.STDOUT,timeout=120)
        output=(artifacts/(name+'-player.log')).read_text(errors='replace')+(artifacts/(name+'-stdout.log')).read_text(errors='replace')
        passed=result.returncode==0 and 'FISHMMO_DB_PROBE_EXIT=0' in output
        summary['Native'+name]={'exitCode':result.returncode,'passed':passed}
        print('Native'+name,summary['Native'+name],flush=True)
        if not passed:
            (artifacts/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
            raise SystemExit('Native probe failed; inspect player logs in '+str(artifacts))
(artifacts/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print('PASS: compatibility checks complete; only read-only SQL was executed.',flush=True)

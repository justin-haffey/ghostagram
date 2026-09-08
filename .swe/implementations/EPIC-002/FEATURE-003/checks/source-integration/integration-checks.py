"""Bounded child-only checks. Invoke only after the parent grants the shared build slot."""
from pathlib import Path
import argparse, hashlib, json, os, shutil, subprocess, time, zipfile, xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[6]
if root.name != 'ghostagram':
    raise RuntimeError('Unexpected child root')
os.chdir(root)
parser = argparse.ArgumentParser()
parser.add_argument('--phase', required=True)
parser.add_argument('--output-root')
parser.add_argument('--generation', required=True)
parser.add_argument('--manifest-sha256', required=True)
parser.add_argument('--build', action='store_true')
parser.add_argument('--packages')
parser.add_argument('--test', choices=['composition', 'ordinary', 'conformance', 'local-projections', 'f004-corrective', 'server-workspace'])
parser.add_argument('--projects', nargs='*')
args = parser.parse_args()
output_root = Path(args.output_root).resolve() if args.output_root else Path(__file__).parent
if root not in output_root.parents:
    raise RuntimeError('Output root must remain inside the Ghostagram repository')
out = output_root / args.phase
out.mkdir(exist_ok=False)
system = root.parent / 'ghostworx-system'
generation = (system / args.generation).resolve()
manifest_path = generation / 'owner-outputs.json'
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
if sha(manifest_path) != args.manifest_sha256:
    raise RuntimeError('Owner manifest does not match the parent-issued generation')
manifest = json.loads(manifest_path.read_text(encoding='utf8'))
owner_outputs = list(manifest['outputs'])
supplement_path = generation / 'owner-outputs-supplement-semantic-links-support.json'
if supplement_path.exists():
    supplement = json.loads(supplement_path.read_text(encoding='utf8'))
    if (supplement.get('originalOwnerManifestSha256') != args.manifest_sha256 or
            supplement.get('sourceFingerprint') != manifest['sourceFingerprint'] or
            supplement.get('sourceFilesStillEqual') is not True or
            supplement.get('project') != 'tests/Ghostworx.System.SemanticLinks.Conformance/Support/Ghostworx.System.SemanticLinks.Conformance.Support.csproj'):
        raise RuntimeError('Owner output supplement does not match the immutable issued generation')
    owner_outputs.append({key: supplement[key] for key in ['project', 'path', 'byteLength', 'sha256']})
results = []

def save(name, value):
    (out / name).write_text(json.dumps(value, indent=2) + '\n', encoding='utf8', newline='\n')

def verify_owners(stage):
    rows = [{'path': row['path'], 'expected': row['sha256'], 'actual': sha(system / row['path'])}
            for row in owner_outputs]
    save('owners-' + stage + '.json', rows)
    if any(row['expected'] != row['actual'] for row in rows):
        raise RuntimeError('A System owner DLL differs from the issued generation')

def verify_copies(name, folder):
    owners = {Path(row['path']).name: row['sha256'] for row in owner_outputs}
    rows = [{'path': str(path.relative_to(root)), 'expected': owners.get(path.name), 'actual': sha(path)}
            for path in sorted(folder.glob('Ghostworx.System*.dll'))]
    save('copies-' + name + '.json', rows)
    save('local-outputs-' + name + '.json', [{'path': str(path.relative_to(root)), 'sha256': sha(path)}
         for path in sorted(folder.glob('Ghostagram*.dll'))])
    if any(row['expected'] != row['actual'] for row in rows):
        raise RuntimeError('A copied System DLL is missing from or differs from the issued owner generation')

def stage_copies(name, folder):
    owners = {Path(row['path']).name: row for row in owner_outputs}
    rows = []
    for path in sorted(folder.glob('Ghostworx.System*.dll')):
        owner = owners.get(path.name)
        if owner is None:
            raise RuntimeError(f'{path.name} is not present in the issued owner manifest')
        before = sha(path)
        source = system / owner['path']
        shutil.copy2(source, path)
        rows.append({'path': str(path.relative_to(root)), 'owner': owner['path'], 'before': before,
                     'after': sha(path), 'expected': owner['sha256']})
    if not rows or any(row['after'] != row['expected'] for row in rows):
        raise RuntimeError('System runtime copies could not be staged from the issued generation')
    save('staged-copies-' + name + '.json', rows)

def run(name, command, timeout=90):
    stdout = out / (name + '.stdout.log')
    stderr = out / (name + '.stderr.log')
    start = time.monotonic()
    forced = None
    fatal = None
    with stdout.open('xb') as so, stderr.open('xb') as se:
        proc = subprocess.Popen(command, stdout=so, stderr=se, creationflags=subprocess.CREATE_NO_WINDOW)
        while proc.poll() is None:
            now = time.monotonic()
            if fatal is None and b'Unhandled exception' in stderr.read_bytes():
                fatal = now
            if now - start > timeout or (fatal is not None and now - fatal > 5):
                forced = 'owned-process-timeout' if fatal is None else 'owned-unhandled-exception-hang'
                proc.kill()
                break
            time.sleep(.1)
        code = proc.wait(timeout=10)
    row = {'name': name, 'command': command, 'exitCode': None if forced else code,
           'ownedProcessExitCode': code, 'forcedTermination': forced,
           'seconds': round(time.monotonic() - start, 3), 'stdout': stdout.name, 'stderr': stderr.name}
    results.append(row)
    save('commands.json', results)
    print(name + ': ' + ('PASS' if code == 0 and not forced else 'FAIL'), flush=True)
    if code != 0 or forced:
        print((stderr.read_text(encoding='utf8', errors='replace') or stdout.read_text(encoding='utf8', errors='replace'))[-5000:], flush=True)
        return False
    return True

verify_owners('before')
paths = sorted(set(subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '--',
    'src', 'tests', 'benchmarks', 'Ghostagram.slnx', 'Directory.Build.props']).decode('utf8').splitlines()))
source_rows = []
with zipfile.ZipFile(out / 'source.zip', 'x', zipfile.ZIP_DEFLATED) as archive:
    for name in paths:
        path = root / name
        if path.is_file():
            data = path.read_bytes()
            source_rows.append({'path': name, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)})
            archive.writestr(name, data)
source_bytes = json.dumps(source_rows, sort_keys=True, separators=(',', ':')).encode('utf8')
(out / 'source-manifest.json').write_bytes(source_bytes)
(out / 'source-fingerprint.txt').write_text(hashlib.sha256(source_bytes).hexdigest() + '\n', encoding='ascii')
save('generation.json', {'path': args.generation, 'manifestSha256': args.manifest_sha256,
                       'sourceFingerprint': manifest['sourceFingerprint'], 'childSourceFingerprint': hashlib.sha256(source_bytes).hexdigest(),
                       'ownerOutputSupplementSha256': sha(supplement_path) if supplement_path.exists() else None,
                       'scope': 'provisional child integration; not acceptance'})
success = True
try:
    if args.build:
        projects = [p.resolve() for folder in ['src', 'tests', 'benchmarks'] for p in (root / folder).glob('*/*.csproj')]
        remaining = set(projects)
        ordered = []
        while remaining:
            ready = [p for p in remaining if not any((p.parent / ref.get('Include').replace('\\', '/')).resolve() in remaining
                     for ref in ET.parse(p).iter('ProjectReference'))]
            if not ready:
                raise RuntimeError('Local project dependency cycle')
            ordered += sorted(ready)
            remaining.difference_update(ready)
        properties = ['-p:BuildProjectReferences=false', '-p:ShouldUnsetParentConfigurationAndPlatform=false']
        if args.packages:
            solution = out / 'Ghostagram.LocalChecks.slnx'
            content = ET.Element('Solution')
            for project in ordered:
                ET.SubElement(content, 'Project', Path=os.path.relpath(project, out).replace('\\', '/'))
            ET.ElementTree(content).write(solution, encoding='utf-8', xml_declaration=False)
            feed = out / 'empty-feed'
            feed.mkdir()
            success = run('restore', ['dotnet', 'restore', str(solution), '--no-dependencies', '--packages', args.packages,
                                     '--source', str(feed), '-p:NuGetAudit=false'] + properties)
        if success:
            for project in ordered:
                if args.projects and project.stem not in args.projects:
                    continue
                if not run('build-' + project.stem, ['dotnet', 'build', str(project.relative_to(root)), '-c', 'Release', '--no-restore'] + properties):
                    success = False
                    break
    if success and args.test:
        dll = lambda name: f'tests/{name}/bin/Release/net10.0/{name}.dll'
        commands = []
        if args.test == 'composition':
            commands = [('composition', ['dotnet', dll('Ghostagram.Bridge.Tests'), '--profile', 'composition'])]
        elif args.test == 'ordinary':
            commands = [(name, ['dotnet', dll(name)]) for name in ['Ghostagram.Core.Tests', 'Ghostagram.Bridge.Tests',
                'Ghostagram.Execution.Tests', 'Ghostagram.Cutover.Tests', 'Ghostagram.Persistence.Tests',
                'Ghostagram.Server.GraphWorkspace.Tests', 'Ghostagram.Layout.Verification']]
        elif args.test == 'local-projections':
            commands = [('local-projections', ['dotnet', dll('Ghostagram.Graph.Conformance'), '--profile', 'composition',
                                              '--local-projections', '--output', str(out / 'projections')])]
        elif args.test == 'conformance':
            commands = [('graph-conformance', ['dotnet', dll('Ghostagram.Graph.Conformance.Tests')]),
                ('variable-conformance', ['dotnet', dll('Ghostagram.Graph.Conformance.Tests'), '--feature', 'feature-002', '--fixtures',
                    '../ghostworx-system/tests/Ghostworx.System.Variable.Conformance/Fixtures/variable/v1']),
                ('links-conformance', ['dotnet', dll('Ghostagram.Graph.Conformance.Tests'), '--feature', 'feature-003', '--corpus',
                    '../ghostworx-system/tests/Ghostworx.System.SemanticLinks.Conformance/Corpus/V1', '--profile',
                    '../ghostworx-system/tests/Ghostworx.System.SemanticLinks.Conformance/Profiles/semantic-link-conformance-v1.json'])]
        elif args.test == 'f004-corrective':
            runner = dll('Ghostagram.Graph.Conformance')
            test = dll('Ghostagram.Graph.Conformance.Tests')
            graph = '../ghostworx-system/tests/Ghostworx.System.Graph.Conformance/Fixtures/semantic-graph/v1'
            graph_schema = '../ghostworx-system/tests/Ghostworx.System.Graph.Conformance/Schemas/conformance-result.schema.json'
            variable = '../ghostworx-system/tests/Ghostworx.System.Variable.Conformance/Fixtures/variable/v1'
            links = '../ghostworx-system/tests/Ghostworx.System.SemanticLinks.Conformance/Corpus/V1'
            link_profile = '../ghostworx-system/tests/Ghostworx.System.SemanticLinks.Conformance/Profiles/semantic-link-conformance-v1.json'
            version = 'corrective-' + hashlib.sha256(source_bytes).hexdigest()[:16]
            commands = [
                ('Ghostagram.Execution.Tests', ['dotnet', dll('Ghostagram.Execution.Tests')]),
                ('Ghostagram.Server.GraphWorkspace.Tests', ['dotnet', dll('Ghostagram.Server.GraphWorkspace.Tests')]),
                ('graph-conformance-tests', ['dotnet', test]),
                ('graph-envelope', ['dotnet', runner, '--corpus', graph, '--schema', graph_schema,
                    '--output', str(out / 'graph-result.json'), '--participant-version', version]),
                ('variable-conformance-tests', ['dotnet', test, '--feature', 'feature-002', '--fixtures', variable]),
                ('variable-envelope', ['dotnet', runner, '--feature', 'feature-002', '--fixtures', variable,
                    '--result', str(out / 'variable-result.json'), '--participant-version', version]),
                ('links-conformance-tests', ['dotnet', test, '--feature', 'feature-003', '--corpus', links,
                    '--profile', link_profile]),
                ('links-envelope', ['dotnet', runner, '--feature', 'feature-003', '--corpus', links,
                    '--profile', link_profile, '--result', str(out / 'links-result.json'), '--participant-version', version])]
        elif args.test == 'server-workspace':
            commands = [('Ghostagram.Server.GraphWorkspace.Tests',
                         ['dotnet', dll('Ghostagram.Server.GraphWorkspace.Tests')])]
        for name, command in commands:
            if args.test in ('f004-corrective', 'server-workspace'):
                stage_copies(name, root / Path(command[1]).parent)
            verify_copies(name, root / Path(command[1]).parent)
            command_success = run(name, command)
            verify_copies(name + '-after', root / Path(command[1]).parent)
            success = command_success and success
finally:
    source_after = []
    for name in paths:
        path = root / name
        if path.is_file():
            data = path.read_bytes()
            source_after.append({'path': name, 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data)})
    save('source-after.json', source_after)
    if source_after != source_rows:
        raise RuntimeError('Ghostagram source changed during the bounded phase')
    verify_owners('after')
raise SystemExit(0 if success else 1)

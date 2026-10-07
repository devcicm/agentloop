"""Validate a local ZIP allowlist, byte hashes and known secret patterns. Prints no secret values."""
import argparse
import hashlib
import importlib.util
import json
import pathlib
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--zip', required=True)
parser.add_argument('--report', required=True)
args = parser.parse_args()
spec = importlib.util.spec_from_file_location('secret_scan', pathlib.Path(__file__).with_name('audit-git-secrets.py'))
scanner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scanner)
errors = []
files = 0
with zipfile.ZipFile(args.zip) as archive:
    names = archive.namelist()
    if len(names) != len(set(names)):
        errors.append({'reason': 'duplicate-entry'})
    manifest = json.loads(archive.read('release-manifest.json'))
    declared = {entry['path']: entry for entry in manifest['files']}
    for name in names:
        parts = pathlib.PurePosixPath(name).parts
        if name.startswith('/') or '\\' in name or ':' in name or '..' in parts or any(p.lower() in {'.git', '.vs', '.agentloop', 'local-secrets', 'tests', 'obj'} for p in parts):
            errors.append({'path': name, 'reason': 'forbidden-path'})
        if name.endswith('/'):
            continue
        files += 1
        data = archive.read(name)
        if name != 'release-manifest.json':
            entry = declared.get(name)
            if entry is None or entry['bytes'] != len(data) or entry['sha256'] != hashlib.sha256(data).hexdigest():
                errors.append({'path': name, 'reason': 'manifest-mismatch'})
        findings, _ = scanner.scan(data, set())
        if findings:
            errors.append({'path': name, 'reason': 'secret-candidate', 'findings': findings})
    for name in declared:
        if name not in names:
            errors.append({'path': name, 'reason': 'missing-entry'})
    if 'AgentLoop.Cli.exe' not in names:
        errors.append({'reason': 'missing-executable'})
report = {'files': files, 'commit': manifest['commit'], 'version': manifest['version'],
          'published': False, 'errors': errors,
          'limitations': 'Pattern scan and manifest comparison; does not prove absence of arbitrary opaque secrets or certify authorship.'}
pathlib.Path(args.report).write_bytes((json.dumps(report, indent=2) + '\n').encode('utf-8'))
print(json.dumps({'files': files, 'errors': len(errors), 'report': args.report}))
raise SystemExit(bool(errors))

"""Compare a corrective F004 run with the independently accepted Ghostagram baseline."""
from pathlib import Path
import argparse, copy, hashlib, json, re

root = Path(__file__).resolve().parents[6]
parser = argparse.ArgumentParser()
parser.add_argument('phase', type=Path)
args = parser.parse_args()
phase = args.phase.resolve()
if root not in phase.parents:
    raise RuntimeError('Phase must be inside the Ghostagram repository')

baseline = root / '.swe/implementations/EPIC-002/FEATURE-004/checks/candidate-release/conformance'
profiles = {
    'graph': ('graph-result.json', {'identity.address-roundtrip', 'federation.origin-preserved',
                                    'federation.mirror-projection-distinct'}),
    'variable': ('variable-result.json', set()),
    'links': ('links-result.json', set()),
}
guid = re.compile(r'(?i)[0-9a-f]{32}')

def normalized(case, dynamic_ids):
    value = copy.deepcopy(case)
    if value.get('caseId') in dynamic_ids:
        fields = value.get('observedFields', {})
        for key, item in list(fields.items()):
            if isinstance(item, str):
                fields[key] = guid.sub('<generated-id>', item)
    return value

report = []
success = True
for profile, (name, dynamic_ids) in profiles.items():
    old = json.loads((baseline / name).read_text(encoding='utf8'))
    new = json.loads((phase / name).read_text(encoding='utf8'))
    old_body = old if 'cases' in old else old['record']
    new_body = new if 'cases' in new else new['record']
    old_cases = {case['caseId']: case for case in old_body['cases']}
    new_cases = {case['caseId']: case for case in new_body['cases']}
    missing = sorted(set(old_cases) - set(new_cases))
    added = sorted(set(new_cases) - set(old_cases))
    differences = []
    for case_id in sorted(set(old_cases) & set(new_cases)):
        left = normalized(old_cases[case_id], dynamic_ids)
        right = normalized(new_cases[case_id], dynamic_ids)
        if left != right:
            differences.append({'id': case_id,
                                'changedFields': sorted(key for key in set(left) | set(right)
                                                        if left.get(key) != right.get(key))})
    old_totals = old_body.get('totals', old_body.get('summary'))
    new_totals = new_body.get('totals', new_body.get('summary'))
    same_totals = old_totals == new_totals
    passed = not missing and not added and not differences and same_totals
    success = success and passed
    report.append({'profile': profile, 'baselineCaseCount': len(old_cases), 'currentCaseCount': len(new_cases),
                   'missing': missing, 'added': added, 'differences': differences,
                   'totalsEqual': same_totals, 'baselineTotals': old_totals,
                   'currentTotals': new_totals, 'generatedGraphIdsNormalized': sorted(dynamic_ids),
                   'passed': passed,
                   'baselineSha256': hashlib.sha256((baseline / name).read_bytes()).hexdigest(),
                   'currentSha256': hashlib.sha256((phase / name).read_bytes()).hexdigest()})

(phase / 'case-comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf8', newline='\n')
raise SystemExit(0 if success else 1)

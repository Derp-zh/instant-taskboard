"""Record runtime and product-source provenance; keep preview scopes distinct."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
for key in ('core', 'compact', 'integration', 'appearance', 'listening', 'publish'):
    parser.add_argument('--' + key, required=True)
for key in ('desktop', 'mouse', 'scroll', 'startup'):
    parser.add_argument('--' + key)
args = parser.parse_args()
accepted = all(getattr(args, key) for key in ('desktop', 'mouse', 'scroll'))
assert accepted or not any(getattr(args, key) for key in ('desktop', 'mouse', 'scroll')), 'desktop evidence must be complete'
root = Path(__file__).resolve().parents[1]
locations, reports = {}, {}
for name, filename, minimum in (('core', 'report.json', 114), ('compact', 'compact-report.json', 38),
                                 ('integration', 'integration-report.json', 17), ('appearance', 'appearance-report.json', 12),
                                 ('listening', 'listening-report.json', 49)):
    path = root / getattr(args, name) / filename
    report = json.loads(path.read_text(encoding='utf-8-sig'))
    assert report['passed'] >= minimum, name
    if name != 'core':
        assert report.get('mouseMoved', False) is False
    reports[name] = report
    locations[name] = path.relative_to(root).as_posix()
publish = root / args.publish
runtime_hashes = {name: hashlib.sha256((publish / name).read_bytes()).hexdigest().upper()
                  for name in ('即时任务看板.exe', 'Taskboard.dll', 'Taskboard.Core.dll')}
tested_core = root / 'tests/Taskboard.Checks/bin/Release/net8.0/Taskboard.Core.dll'
assert hashlib.sha256(tested_core.read_bytes()).hexdigest().upper() == runtime_hashes['Taskboard.Core.dll']
summary = {
    'version': '0.9.1', 'releaseLabel': 'preview',
    'date': datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8))).isoformat(),
    'status': 'implemented; offscreen checks passed; desktop acceptance deferred by user',
    'requirements': [f'U{u}/A{u+18}' for u in range(55, 69)],
    'checks': {name: report['passed'] for name, report in reports.items()},
    'evidence': locations,
    'programSHA256': runtime_hashes,
    'corePerformance': reports['core'].get('performance'),
    'lastOffscreenSwapPreparationMs': reports['compact'].get('lastSwapPreparationMs'),
    'renderArtifacts': [args.compact + '/' + name for name in ('formal-card.png', 'quick-entry.png', 'numbered-stack.png')],
    'evidenceScopes': {
        'core': 'Task history, paths, persistence and backwards-compatible draft creation time; exact tested Core DLL matches publish output.',
        'compact': 'Real WPF offscreen layout, hit structure, compositor setup/completion, quick entry save, direct outside-click policy calls, draft numbering, deletion and reopen.',
        'nativeObserver': 'Registration and immediate disposal checked; physical outside clicks were not executed.',
        'animation': '100ms compositor timeline and interruption/cleanup checked offscreen; actual displayed intermediate frames, perceived fluency and flicker were not sampled.'
    },
    'boundaries': [
        'User declined desktop/mouse coordination for now; no real mouse or on-screen acceptance was performed.',
        'Only isolated task and configuration data was used; no real task data or notification sounds changed.',
        'No real startup entry update, shortcut creation, reboot or login was performed for this preview.',
        '0.9.0 desktop evidence remains historical; it does not validate this preview runtime.',
        'Real notification sound replacement, Codex desktop live bridge and Feishu cloud sync remain separate pending work.'
    ],
    'pendingDesktopAcceptance': 'docs/ACCEPTANCE-0.9.1.md'
}
if accepted:
    assert reports['compact']['passed'] >= 45
    for name, filename, minimum, count_key in (('desktop', 'ui-report.json', 77, 'passed'), ('mouse', 'desktop-report.json', 64, 'Passed'), ('scroll', 'scroll-report.json', 39, 'passed')):
        path = root / getattr(args, name) / filename
        value = json.loads(path.read_text(encoding='utf-8-sig'))
        assert value[count_key] >= minimum, name
        summary['checks'][name] = value[count_key]
        summary['evidence'][name] = path.relative_to(root).as_posix()
    for name in ('motion', 'deal', 'animation-pixels', 'draft-host', 'glass'):
        path = root / args.desktop / (name + '-report.json' if name not in ('animation-pixels',) else name + '.json')
        value = json.loads(path.read_text(encoding='utf-8-sig'))
        if name == 'motion': assert all(item['passed'] for item in value['reports'])
        elif name == 'draft-host': assert value['clearOutlineInterior'] and value['singleNativeWindow']
        else: assert value['passed']
        summary['evidence'][name] = path.relative_to(root).as_posix()
        if name == 'deal': summary['dealAnimation'] = {k: v for k, v in value.items() if k != 'frames'}
        if name == 'animation-pixels': summary['displayRefreshHz'] = value['refreshHz']
    if args.startup:
        path = root / args.startup / 'startup-entry-report.json'
        value = json.loads(path.read_text(encoding='utf-8-sig')); assert value['passed'] >= 3
        summary['checks']['startup'] = value['passed']; summary['evidence']['startup'] = path.relative_to(root).as_posix()
    summary['releaseLabel'] = ''
    provenance_path = root / 'test-output/compact091-offscreen-assertion-provenance.json'
    provenance = json.loads(provenance_path.read_text(encoding='utf-8'))
    for source, expected in provenance['normalProductSource'].items():
        assert hashlib.sha256((root / source).read_bytes()).hexdigest().upper() == expected, source
    assert summary['dealAnimation']['LastContentSwapPreparationMs'] < 200, 'actual desktop preparation'
    summary['desktopEvidenceRuntimeDllSHA256'] = provenance['testedDesktopDllSHA256']
    summary['postDesktopVerificationChange'] = {
        'files': provenance['allowedChanges'],
        'scope': provenance['reason'],
        'normalProductSourceUnchanged': True,
        'proof': provenance_path.relative_to(root).as_posix()
    }
    summary['status'] = 'isolated rule, offscreen, desktop pixel and physical input checks passed'
    summary['evidenceScopes']['nativeObserver'] = 'Physical external-app clicks tested: first stays visible without white flash, inside resets, second closes and retains edit buffer.'
    summary['evidenceScopes']['animation'] = 'Actual desktop samples confirm two separated card spans, lift, same native host, original bounds restored and no unexpected host hides; composition remains active while UI is busy. Samples are not exhaustive 120fps measurement.'
    summary['boundaries'] = [
        'Only isolated task/configuration data was used; running user versions were preserved and actual application notification sounds were not changed.',
        'No Windows reboot/logout was performed; startup entry readback is separate from a login test.',
        'Real notification sound replacement, Codex desktop live bridge and Feishu cloud sync remain separate pending work.'
    ]
    summary.pop('pendingDesktopAcceptance', None)
output = root / ('docs/VERIFICATION-0.9.1.json' if accepted else 'docs/VERIFICATION-0.9.1-preview.json')
output.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'version': summary['version'], 'checks': summary['checks'], 'desktopAcceptance': 'passed' if accepted else 'pending'}, ensure_ascii=False))

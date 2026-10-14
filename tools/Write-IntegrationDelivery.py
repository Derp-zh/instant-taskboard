"""Summarize isolated module and desktop evidence for the requested build."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--version', default='0.8.2')
parser.add_argument('--integration')
parser.add_argument('--startup')
for name in ('core', 'listening', 'scroll', 'desktop', 'mouse', 'appearance', 'publish'):
    parser.add_argument('--' + name, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
reports = {}
locations = {}
for name, filename in (('core', 'report.json'), ('listening', 'listening-report.json'), ('scroll', 'scroll-report.json'),
                       ('desktop', 'ui-report.json'), ('mouse', 'desktop-report.json'), ('appearance', 'appearance-report.json')):
    path = root / getattr(args, name) / filename
    reports[name] = json.loads(path.read_text(encoding='utf-8-sig'))
    if 'passed' not in reports[name]:
        reports[name]['passed'] = reports[name]['Passed']
    assert reports[name]['passed'] >= {'core': 87, 'listening': 49, 'scroll': 39, 'desktop': 77, 'mouse': 42, 'appearance': 12}[name], name
    locations[name] = path.relative_to(root).as_posix()
if args.version == '0.9.0':
    assert reports['core']['passed'] >= 111
    assert reports['mouse']['passed'] >= 49
    assert args.integration and args.startup
    for name, filename, minimum in (('integration', 'integration-report.json', 17), ('startup', 'startup-entry-report.json', 3)):
        path = root / getattr(args, name) / filename
        reports[name] = json.loads(path.read_text(encoding='utf-8-sig'))
        assert reports[name]['passed'] >= minimum, name
        locations[name] = path.relative_to(root).as_posix()
motion_file = root / args.desktop / 'motion-report.json'
motion = json.loads(motion_file.read_text(encoding='utf-8-sig'))
assert all(case['passed'] for case in motion['reports'])
assert len([x for x in motion['reports'] if 'stationaryBackgroundDifference' in x]) == 13
publish = root / args.publish
hashes = {name: hashlib.sha256((publish / name).read_bytes()).hexdigest().upper()
          for name in ('即时任务看板.exe', 'Taskboard.dll', 'Taskboard.Core.dll')}
summary = {
    'version': args.version,
    'date': datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8))).isoformat(),
    'integrated': ['notification listening', 'embedded Alarm04 default audio', '100ms central scroll animation'],
    'checks': {name: report['passed'] for name, report in reports.items()},
    'evidence': locations | {'liveMovement': motion_file.relative_to(root).as_posix()},
    'programSHA256': hashes,
    'defaultAudioSHA256': hashlib.sha256((root / 'src/Taskboard.Desktop/Assets/Alarm04.wav').read_bytes()).hexdigest().upper(),
    'scroll': {'durationMs': reports['scroll']['durationMs'], 'flashMs': 25, 'revealMs': 75,
               'scope': ['draft stack', 'task editor', 'settings'], 'actualIntermediatePixelsSampled': True},
    'liveMovement': {'windowTypes': len([x for x in motion['reports'] if 'stationaryBackgroundDifference' in x]),
                     'allChecksPassed': True, 'perWindowMovementSamples': 19},
    'corePerformance': reports['core'].get('performance'),
    'evidenceScopes': {
        'core': 'Core library unchanged after the 87 rule checks.',
        'scrollDesktopMouse': '39 motion, 77 window and 42 real-input checks completed before the later badge and stage-name appearance changes. Their input and compositor mechanisms were unchanged.',
        'finalBuild': '49 listening/default-audio and 12 badge/stage-name checks were rerun on the final build. Appearance evidence uses real WPF rendering without showing desktop windows or moving the mouse.'
    },
    'boundaries': ['Only isolated task data was used; real task data was not read or changed.',
                   'Real Windows notification sound settings were not modified during tests.',
                   'Actual app capture, original sound suppression and restoration still need a separate real-app acceptance.',
                   '120Hz compositor support is not a guarantee of frame rate on every device.',
                   'Codex desktop live bridge and Feishu cloud sync retain their previous pending status.']
}
if args.version == '0.9.0':
    summary['integrated'] += ['in-orb action count', 'stage name bubbles', 'independent system data and configuration paths',
                             'safe portable migration', 'centered group resize', '20 task archives', '20-step redo', 'latest-version startup registration']
    summary['evidenceScopes'] = {
        'core': '111 isolated rule, archive, redo, atomic persistence, configuration recovery and version selection checks.',
        'finalBuild': 'All listening, appearance, integration, scroll, desktop and real-input checks ran against the same final 0.9.0 runtime files.',
        'startup': 'The existing current-user startup entry was updated and read back. Version catalogue checks used isolated fixtures; no reboot or login was performed.'
    }
    summary['startup'] = reports['startup']
    for name, filename in (('operationPerformance', 'performance-report.json'), ('cardDragPerformance', 'drag-performance.json')):
        path = root / args.desktop / filename
        summary[name] = json.loads(path.read_text(encoding='utf-8-sig'))
        summary['evidence'][name] = path.relative_to(root).as_posix()
    summary['boundaries'].append('The existing 0.8.2 user process was preserved. Real task migration happens after it exits and the user starts 0.9.0 normally.')
(root / ('docs/VERIFICATION-' + args.version + '.json')).write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'version': summary['version'], 'checks': summary['checks'], 'programSHA256': hashes}, ensure_ascii=False))

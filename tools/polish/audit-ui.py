"""Inventory every authored UI surface; generated/build files are excluded."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET
import sys
sys.stdout.reconfigure(encoding='utf-8')

root = Path(__file__).resolve().parents[2]
app = root / 'src/MelodayMusic.App'
report = []
for folder in ['Views', 'Controls', 'Themes']:
    for path in sorted((app / folder).glob('*')):
        if path.suffix not in ['.xaml', '.cs']:
            continue
        source = path.read_text(encoding='utf-8-sig')
        if '--fix-whitespace' in sys.argv:
            cleaned = '\n'.join(line.rstrip() for line in source.splitlines()) + '\n'
            if cleaned != source:
                path.write_text(cleaned, encoding='utf-8')
                source = cleaned
        data = {'file': str(path.relative_to(app)), 'literal_text': [], 'local_geometry': [], 'unshared_controls': []}
        if path.suffix == '.xaml':
            xml = ET.fromstring(source)
            for node in xml.iter():
                tag = node.tag.split('}')[-1]
                for key in ['Text', 'Content', 'Subtitle', 'Subheader', 'EmptyText', 'ToolTipService.ToolTip']:
                    value = node.get(key)
                    if value and not value.startswith('{'):
                        data['literal_text'].append(f'{tag}: {value}')
                for key in ['FontFamily', 'FontSize', 'CornerRadius', 'Width', 'Height', 'Spacing', 'Margin', 'Padding']:
                    value = node.get(key)
                    if value and not value.startswith('{') and value not in ['Auto', '*', '0', '2', '4', '8', '12', '16', '20', '24', '32', '40', '48', '56', '64', '999']:
                        data['local_geometry'].append(f'{tag} {key}={value}')
                if tag in ['Button', 'ToggleButton', 'TextBlock'] and node.get('Style') is None:
                    data['unshared_controls'].append(tag + ': ' + (node.get('Text') or node.get('Content') or 'custom content'))
        else:
            data['literal_text'] = re.findall(r'(?:Dialogs\.(?:Text|Note)|Text|Content|Subtitle)\s*(?:\(|=)\s*"([^"\n]+)"', source)
            data['local_geometry'] = re.findall(r'(?:FontSize|CornerRadius|Width|Height|Spacing|MinWidth|MaxHeight)\s*=\s*(?:new\([^)]*\)|\d+)', source)
        report.append(data)
out = root / 'artifacts/polish-audit.json'
out.parent.mkdir(exist_ok=True)
out.write_text(json.dumps(report, indent=2), encoding='utf-8')
for item in report:
    if item['literal_text'] or item['unshared_controls']:
        print(item['file'])
        print(' | '.join(item['literal_text']))
        if item['unshared_controls']: print('UNSHARED: ' + ' | '.join(item['unshared_controls']))
print(f'{len(report)} UI source files inventoried; all XAML parsed. Report: {out}')

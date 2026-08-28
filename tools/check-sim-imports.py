"""심(시뮬레이션) 코드가 뷰·UI 쪽을 import하지 않는지 검사한다 — asmdef로 컴파일러가 강제하기 전(5단계)까지의 안전망.

usage: python tools/check-sim-imports.py   (exit 1 = 위반)

규칙
  Assets/Scripts/Runtime/Sim/**             : CoreDawn.Sim 외의 CoreDawn.* import 금지
  Assets/Scripts/Runtime/Factory/** (심 파일): CoreDawn.Entities import 금지 (뷰 타입)
      — MonoBehaviour 브리지(FactoryBootstrap·PlacementBridge·CoreBootstrap·MachineProcessor·BeltItemView 등)는 제외.
      UI·FPS(IInteractiveBehavior.Interact → 화면 열기)는 알려진 빚이라 아직 허용한다: 4단계에서 끊는다.
"""
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Assets', 'Scripts', 'Runtime')
USING = re.compile(r'^\s*using\s+(CoreDawn\.[\w.]+)\s*;', re.M)

# 공장 폴더 안의 Unity 접점(브리지·뷰) — 뷰를 알아도 되는 파일
FACTORY_BRIDGES = {
    'FactoryBootstrap.cs', 'PlacementBridge.cs', 'CoreBootstrap.cs', 'MachineProcessor.cs',
    'BeltItemView.cs', 'BaseProcessor.cs',
}
FACTORY_FORBIDDEN = {'CoreDawn.Entities'}

violations = []


def scan(folder, allowed=None, forbidden=None, skip=()):
    for dirpath, _, files in os.walk(folder):
        for name in files:
            if not name.endswith('.cs') or name in skip:
                continue
            path = os.path.join(dirpath, name)
            text = open(path, 'rb').read().decode('utf-8', 'replace')
            for m in USING.finditer(text):
                ns = m.group(1)
                bad = (allowed is not None and not ns.startswith(allowed)) or (forbidden and ns in forbidden)
                if bad:
                    rel = os.path.relpath(path, os.path.join(ROOT, '..', '..', '..')).replace(os.sep, '/')
                    violations.append(f'{rel}: using {ns};')


scan(os.path.join(ROOT, 'Sim'), allowed='CoreDawn.Sim')
scan(os.path.join(ROOT, 'Factory'), forbidden=FACTORY_FORBIDDEN, skip=FACTORY_BRIDGES)

if violations:
    print('sim -> presentation import violations:')
    for v in violations:
        print('  ' + v)
    sys.exit(1)
print('ok: no sim → presentation imports')

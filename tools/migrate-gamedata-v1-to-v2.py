"""GameData.json(v1, SO 임포터용) → 팩 data.json(v2, SimDatabase용). id는 저장하지 않고 키로 파생.
출력: Assets/StreamingAssets/packs/coredawn/data.json, tools/id-migration-v1-v2.json (옛 id → 새 id)."""
import json, re, os, collections
REPO = r"c:\Users\niced\Documents\Projects\GitHub\Univ\TeamProj2606"
SRC = os.path.join(REPO, 'Assets', 'Data', 'Import', 'GameData.json')
OUT = os.path.join(REPO, 'Assets', 'StreamingAssets', 'packs', 'coredawn', 'data.json')
MAP_OUT = os.path.join(REPO, 'tools', 'id-migration-v1-v2.json')
PACK = 'coredawn'

d = json.load(open(SRC, encoding='utf-8'))


def snake(name):
    name = re.sub(r'^Recipe_', '', name)
    s = re.sub(r'(?<=[a-z0-9])(?=[A-Z])', '_', name)
    s = re.sub(r'(?<=[A-Z])(?=[A-Z][a-z])', '_', s)
    return s.lower()


SECTION_OF = {'Item': 'item', 'Recipe': 'recipe', 'Effect': 'effect', 'Building': 'entity', 'Monster': 'entity',
              'Wave': 'wave', 'Gun': 'gun', 'Tutorial': 'tutorial'}
idmap = {}


def newid(old):
    if old in idmap:
        return idmap[old]
    prefix, name = old.split(':', 1)
    nid = f'{PACK}:{SECTION_OF[prefix]}/{snake(name)}'
    idmap[old] = nid
    return nid


def key_of(old):
    return newid(old).split('/', 1)[1]


# 먼저 모든 id를 등록(참조 치환용)
for sec in ['items', 'recipes', 'effects', 'buildings', 'monsters', 'waves', 'guns', 'tutorial']:
    for e in d.get(sec, []):
        newid(e['id'])
assert len(set(idmap.values())) == len(idmap), 'id 충돌: ' + str([k for k, v in collections.Counter(idmap.values()).items() if v > 1])


def uses(lst):
    return [{k: v for k, v in {'effect': newid(u['effect']), 'value': u.get('value', 0), 'duration': u.get('duration', 0),
                               'tickInterval': u.get('tickInterval', 0)}.items() if not (k in ('duration', 'tickInterval') and not v)}
            for u in (lst or [])]


def amounts(lst):
    return [{'item': newid(a['item']), 'amount': a['amount']} for a in (lst or [])]


def head(e):
    h = {'displayName': e.get('displayName', '')}
    if e.get('description'):
        h['description'] = e['description']
    return h


out = {'format': 2, 'pack': PACK, 'items': {}, 'recipes': {}, 'effects': {}, 'entities': {}, 'waves': {}, 'guns': {}, 'tutorial': {}}

# items
for it in d['items']:
    o = head(it)
    o.update({'type': it['type'], 'line': it['line'], 'maxStack': it['maxStack']})
    if it.get('hideFromMenu'):
        o['hideFromMenu'] = True
    mods = []
    if it.get('attackEffects') or it.get('speed', -1) >= 0:
        mods.append({'type': 'Ammo', 'speed': it['speed'], 'gravity': it['gravity'], 'explosionRadius': it['explosionRadius'],
                     'lifetime': it['lifetime'], 'pierce': it['pierce'], 'effects': uses(it.get('attackEffects'))})
    if it.get('gun'):
        mods.append({'type': 'Weapon', 'gun': newid(it['gun'])})
    o['modules'] = mods
    o['view'] = {'icon': it.get('icon'), 'iconGuid': it.get('iconGuid')}
    out['items'][key_of(it['id'])] = o
    # 광맥 — Ore 아이템마다 entities/<item>_deposit (채굴 시간은 원광이 갖는다)
    if it['type'] == 'Ore':
        assert it.get('extractInterval', -1) > 0, f"Ore {it['id']}에 extractInterval 없음"
        out['entities'][key_of(it['id']) + '_deposit'] = {'displayName': it.get('displayName', '') + ' 광맥', 'faction': 'Neutral',
            'modules': [{'type': 'ResourceDeposit', 'resource': newid(it['id']), 'extractInterval': it['extractInterval']}]}

# recipes
for r in d['recipes']:
    o = head(r)
    o.update({'tier': r['tier'], 'seconds': r['craftTime'], 'inputs': amounts(r['inputs']), 'outputs': amounts(r['outputs'])})
    out['recipes'][key_of(r['id'])] = o

# effects
for e in d['effects']:
    o = head(e)
    o['type'] = e['kind']
    if e.get('duration'):
        o['duration'] = e['duration']
    if e.get('tickInterval'):
        o['tickInterval'] = e['tickInterval']
    if e.get('stacking') and e['stacking'] != 'Refresh':
        o['stacking'] = e['stacking']
    if e.get('knockbackMode') and e['knockbackMode'] != 'Directional':
        o['knockbackMode'] = e['knockbackMode']
    if e.get('affects'):
        o['affects'] = [newid(a) for a in e['affects']]
    out['effects'][key_of(e['id'])] = o

# buildings → entities
FACTION = {'Nest': 'Monster', 'Tree': 'Neutral'}
for b in d['buildings']:
    kind = b['kind']
    name = b['id'].split(':', 1)[1]
    o = head(b)
    o['faction'] = FACTION.get(kind, 'Player')
    mods = []
    building = {'type': 'Building', 'size': b['size'], 'category': b.get('category')}
    if b.get('hideFromBuildMenu'):
        building['placeable'] = False
    if not b.get('isDemolishable', True):
        building['isDemolishable'] = False
    if not b.get('isAttackable', True):
        building['isAttackable'] = False
    for k in ('requiredCoreTier', 'threatSeedCost', 'menuOrder'):
        if b.get(k):
            building[k] = b[k]
    if b.get('buildCost'):
        building['cost'] = amounts(b['buildCost'])
    mods.append(building)
    mods.append({'type': 'Health', 'maxHp': b['maxHp']})
    mods.append({'type': 'Effects'})
    if b.get('ports'):
        mods.append({'type': 'Ports', 'ports': b['ports']})
    if b.get('inputSlots') or b.get('outputSlots'):
        inv = {'type': 'Inventory'}
        if b.get('inputSlots'): inv['input'] = b['inputSlots']
        if b.get('outputSlots'): inv['output'] = b['outputSlots']
        if b.get('bufferStackCap'): inv['stackCap'] = b['bufferStackCap']
        mods.append(inv)
    if kind == 'Belt':
        mods.append({'type': 'Conveyor', 'speedTilesPerSec': b.get('speedTilesPerSec', 1.0)})
    elif kind == 'Miner':
        mods.append({'type': 'Extractor', 'speedMultiplier': b.get('speedMultiplier', 1.0)})
    elif kind == 'Assembler':
        mods.append({'type': 'Crafter', 'manual': False, 'speed': b.get('speedMultiplier') or 1.0,
                     'recipes': [newid(r) for r in b.get('availableRecipes', [])]})
    elif kind in ('Splitter', 'Merger'):
        mods.append({'type': 'Router', 'mode': 'split' if kind == 'Splitter' else 'merge'})
    elif kind == 'Core':
        tiers = []
        for t in b.get('tiers', []):
            tiers.append({'name': t.get('name'), 'description': t.get('description'), 'requirements': amounts(t.get('requirements')),
                          'unlocks': t.get('unlocks', []), 'maxHpBonus': t.get('maxHpBonus', 0), 'isFinal': t.get('isFinal', False)})
        mods.append({'type': 'Core', 'tiers': tiers})
    elif kind == 'Nest':
        mods.append({'type': 'NestSpawner'})
    elif kind == 'Tower':
        if name == 'Fence':
            mods.append({'type': 'Blocker'})
        elif name == 'Mine':
            mods.append({'type': 'Trigger', 'radius': b.get('range', 2.0), 'once': True, 'effects': []})
            mods.append({'type': 'AmmoConsumer', 'ammoFilter': [newid(a) for a in b.get('ammoFilter', [])], 'damageMultiplier': b.get('damageMultiplier', 1.0)})
        elif name == 'SlowFieldTower':
            mods.append({'type': 'AuraEmitter', 'radius': b.get('range', 5.0), 'interval': 1.0 / b['fireRate'] if b.get('fireRate') else 1.0, 'effects': []})
            mods.append({'type': 'AmmoConsumer', 'ammoFilter': [newid(a) for a in b.get('ammoFilter', [])], 'damageMultiplier': b.get('damageMultiplier', 1.0)})
        else:
            mods.append({'type': 'TowerBrain', 'range': b.get('range', 8.0), 'minRange': b.get('minRange', 0.0), 'fireRate': b.get('fireRate', 1.0),
                         'turnSpeed': b.get('turnSpeed', 180.0), 'aimTolerance': b.get('aimTolerance', 5.0),
                         'preferHighArc': b.get('preferHighArc', False), 'muzzleHeight': b.get('muzzleHeight', 1.0)})
            mods.append({'type': 'AmmoConsumer', 'ammoFilter': [newid(a) for a in b.get('ammoFilter', [])], 'damageMultiplier': b.get('damageMultiplier', 1.0)})
    elif kind == 'DronePort':
        mods.append({'type': 'DronePort', 'carryCapacity': b.get('carryCapacity', 10), 'droneRange': b.get('droneRange', 20.0), 'travelSpeed': b.get('travelSpeed', 5.0)})
    elif kind == 'Tree':
        pass   # 나무 = Neutral + Building(placeable false) + Health. 드롭은 프리팹(dropItem)에 있어 5a-2에서 Loot로 옮긴다
    elif kind == 'Storage':
        pass   # Inventory만
    else:
        raise SystemExit('unknown building kind ' + kind)
    # 사망 드롭: 정의된 목록(둥지의 괴수핵) 또는 그릇 내용물(버퍼가 있는 건물은 기본으로 떨군다)
    if b.get('drops') or b.get('inputSlots', 0) > 0 or b.get('outputSlots', 0) > 0:
        loot = {'type': 'Loot'}
        if b.get('drops'): loot['drops'] = amounts(b['drops'])
        mods.append(loot)
    o['modules'] = mods
    view = {k: b[k] for k in ('model', 'modelGuid', 'modelCurveL', 'modelCurveLGuid', 'modelCurveR', 'modelCurveRGuid') if b.get(k)}
    if view:
        o['view'] = view
    out['entities'][key_of(b['id'])] = o

# monsters → entities
for m in d['monsters']:
    o = head(m)
    o['faction'] = 'Monster'
    o['modules'] = [
        {'type': 'Health', 'maxHp': m['maxHp']},
        {'type': 'Effects'},
        {'type': 'Movement', 'moveSpeed': m['moveSpeed'], 'rotateSpeed': m['rotateSpeed'], 'crowdRadius': m['crowdRadius'],
         'knockbackDamping': m['knockbackDamping'], 'stickToGround': m['stickToGround']},
        {'type': 'Attack', 'range': m['attackRange'], 'cooldown': m['attackCooldown'], 'effects': uses(m.get('attackEffects'))},
        {'type': 'MonsterBrain', **{k: m[k] for k in ('maxPatience', 'patienceRadius', 'outsidePatienceDrain', 'rangedPokePatienceDrain',
                                                       'patienceRecoverRate', 'absoluteLeashMultiplier', 'returnRegenPerSecond', 'returnTimeout') if k in m}},
    ]
    o['view'] = {'prefab': m.get('prefab'), 'prefabGuid': m.get('prefabGuid')}
    out['entities'][key_of(m['id'])] = o


# player → entities/player (HP·가방·핫바) — SO 없는 유일한 엔티티
if 'player' in d:
    p = d['player']
    out['entities']['player'] = {'displayName': p.get('displayName', '플레이어'), 'faction': 'Player', 'modules': [
        {'type': 'Health', 'maxHp': p.get('maxHp', 300)}, {'type': 'Effects'},
        {'type': 'Inventory', 'main': p.get('main', 25), 'hotbar': p.get('hotbar', 7)},
        {'type': 'Crafter', 'manual': True, 'speed': 1.0, 'recipes': []}]}

# waves
for w in d['waves']:
    o = head(w)
    o.update({'day': w['day'], 'requiredCoreTier': w['requiredCoreTier'], 'baseAmount': w['baseAmount'], 'maxAliveAmount': w['maxAliveAmount'],
              'spawnInterval': w['spawnInterval'], 'monster': newid(w['monster']), 'buffs': uses(w.get('buffs'))})
    out['waves'][key_of(w['id'])] = o

# guns · tutorial — 원본 그대로(id만 치환)
def remap(x):
    if isinstance(x, str) and x in idmap:
        return idmap[x]
    if isinstance(x, list):
        return [remap(v) for v in x]
    if isinstance(x, dict):
        return {k: remap(v) for k, v in x.items() if k != 'id'}
    return x


for g in d['guns']:
    out['guns'][key_of(g['id'])] = remap(g)
for t in d['tutorial']:
    out['tutorial'][key_of(t['id'])] = remap(t)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump(out, open(OUT, 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=2)
json.dump(dict(sorted(idmap.items())), open(MAP_OUT, 'w', encoding='utf-8', newline='\n'), ensure_ascii=False, indent=2)
print('wrote', OUT, {k: len(v) for k, v in out.items() if isinstance(v, dict)})
print('id map', len(idmap), '->', MAP_OUT)

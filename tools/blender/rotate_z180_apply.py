"""원본 .blend 방향 교정 — Blender 표준 규약(정면 −Y → Unity +Z, glTF/FBX 기본 forward −Z)에 맞춘다.

배경(2026-09-01): 팀 FBX 프리셋이 axis_forward='Z'(기본 -Z)였고, 건물 원본은 그 규약(Blender +X = Unity +X)으로 저작돼
표준 내보내기(glTF)에서는 Y축 180° 돌아 보였다. 이 스크립트는 파일의 최상위 오브젝트 전부를 Z축 180° 돌리고 회전을 메시·셰이프키에
적용해(오브젝트 회전은 0으로 남김) 원본 자체를 표준 규약으로 바꾼다. 아마추어가 있는 파일은 손대지 않는다(리그·액션은 별도 판단).

사용: blender -b <file.blend> --python rotate_z180_apply.py -- [--dry]
"""
import bpy, sys, math, mathutils

dry = '--dry' in sys.argv

def world_center(o):
    bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    return tuple(round(sum(v[i] for v in bb) / 8, 3) for i in range(3))

def descendants(o, acc):
    acc.append(o)
    for c in o.children: descendants(c, acc)

if any(o.type == 'ARMATURE' for o in bpy.data.objects):
    print("SKIP: 아마추어가 있는 파일 — 리그·액션 포함 회전은 별도 판단"); sys.exit(0)

# 편집 모드로 저장된 파일이면 오브젝트 모드로(오퍼레이터 poll)
if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
roots = [o for o in bpy.data.objects if o.parent is None]
meshes_before = {o.name: world_center(o) for o in bpy.data.objects if o.type == 'MESH'}
belt_dir_before = {}
for o in bpy.data.objects:
    if o.type == 'MESH' and o.data.shape_keys and len(o.data.shape_keys.key_blocks) > 10:
        kb = o.data.shape_keys.key_blocks; b = kb[0].data; k = kb[10].data
        belt_dir_before[o.name] = round(sum(k[i].co.x - b[i].co.x for i in range(len(b))) / len(b), 4)

R = mathutils.Matrix.Rotation(math.radians(180), 4, 'Z')
for r in roots:
    r.matrix_world = R @ r.matrix_world

# 회전을 루트의 메시에만 굽는다 — 자식은 선택하지 않는다: Blender가 자식의 matrix_parent_inverse를 보정해 월드 위치(이미 돌아간)를 지키고,
# 자식의 로컬 값·애니메이션 키(로컬 기준)는 그대로 남는다(자식까지 구우면 애니메이션 키와 어긋난다 — Smelter의 Molten/Pot에서 확인).
bpy.ops.object.select_all(action='DESELECT')
targets = []
for r in roots: descendants(r, targets)
hidden = [o for o in roots if o.hide_get()]   # 숨긴(눈 아이콘) 오브젝트는 선택이 안 돼 적용에서 빠진다 — 잠시 풀었다가 되돌린다
for o in hidden: o.hide_set(False)
for r in roots:
    if r.type == 'MESH' and r.data.users > 1: r.data = r.data.copy()   # 공유 메시는 적용이 거부된다 — 단일 사용자로
    r.select_set(True)
bpy.context.view_layer.objects.active = next((o for o in roots if o.type == 'MESH'), roots[0])
bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
for o in hidden: o.hide_set(True)

bad = [o.name for o in roots if o.type in ('MESH', 'CURVE') and any(abs(a) > 1e-4 for a in o.rotation_euler)]
if bad: print("WARN: 회전이 0이 아닌 오브젝트(적용 실패?):", bad)

for o in bpy.data.objects:
    if o.type != 'MESH': continue
    b = meshes_before[o.name]; a = world_center(o)
    ok = abs(a[0] + b[0]) < 1e-3 and abs(a[1] + b[1]) < 1e-3 and abs(a[2] - b[2]) < 1e-3
    print(f"OBJ {o.name}: {b} -> {a} {'OK' if ok else 'CHECK'}")
for name, before in belt_dir_before.items():
    o = bpy.data.objects[name]; kb = o.data.shape_keys.key_blocks; b = kb[0].data; k = kb[10].data
    after = round(sum(k[i].co.x - b[i].co.x for i in range(len(b))) / len(b), 4)
    print(f"SHAPEKEY {name}: delta x {before} -> {after} {'OK' if abs(after + before) < 1e-4 else 'CHECK'}")

if dry: print("DRY RUN — 저장 안 함")
else:
    bpy.ops.wm.save_mainfile(); print("SAVED", bpy.data.filepath)

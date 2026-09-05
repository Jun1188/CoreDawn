import bpy, sys, os, json, re, mathutils

argv = sys.argv[sys.argv.index('--')+1:]
cfg = json.load(open(argv[0]))
OUT = cfg['out']
report = {'model': cfg['model'], 'out': OUT, 'clips': [], 'problems': []}

def prob(msg):
    print("PROBLEM:", msg)
    report['problems'].append(msg)

IMP = dict(automatic_bone_orientation=False, ignore_leaf_bones=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
print("FPS", sc.render.fps)

# ---------- model ----------
bpy.ops.import_scene.fbx(filepath=cfg['model'], use_anim=False, **IMP)
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
if len(arms) != 1:
    prob("expected 1 armature in model, got %r" % [o.name for o in arms])
ARM = arms[0]

def root_of(o):
    while o.parent:
        o = o.parent
    return o

ROOT = root_of(ARM)
model_objs = set()

def rec(o):
    model_objs.add(o.name)
    for c in o.children:
        rec(c)

rec(ROOT)
BONES = set(b.name for b in ARM.data.bones)
report['armature'] = ARM.name
report['root_node'] = ROOT.name
report['bone_count'] = len(BONES)
report['root_bones'] = [b.name for b in ARM.data.bones if b.parent is None]
report['hierarchy'] = sorted((o, bpy.data.objects[o].type) for o in model_objs)
report['root_scale'] = [round(v, 5) for v in ROOT.scale]
meshes = [bpy.data.objects[n] for n in model_objs if bpy.data.objects[n].type == 'MESH']
report['meshes'] = [{'name': m.name, 'verts': len(m.data.vertices),
                     'materials': [x.name if x else None for x in m.data.materials]} for m in meshes]
for n in model_objs:
    o = bpy.data.objects[n]
    if o.animation_data:
        o.animation_data_clear()

mn = [1e18]*3
mx = [-1e18]*3
for m in meshes:
    for v in m.bound_box:
        w = m.matrix_world @ mathutils.Vector(v)
        for i in range(3):
            mn[i] = min(mn[i], w[i])
            mx[i] = max(mx[i], w[i])
report['blender_bbox_min'] = [round(x, 4) for x in mn]
report['blender_bbox_max'] = [round(x, 4) for x in mx]
report['blender_size_xyz'] = [round(mx[i]-mn[i], 4) for i in range(3)]
report['height_m_restpose'] = round(mx[2]-mn[2], 4)
print("MODEL", ARM.name, "bones", len(BONES), "height", report['height_m_restpose'])

if ARM.animation_data is None:
    ARM.animation_data_create()
AD = ARM.animation_data
for t in list(AD.nla_tracks):
    AD.nla_tracks.remove(t)
AD.action = None

def obj_slot(act):
    try:
        for s in act.slots:
            if s.target_id_type == 'OBJECT':
                return s
    except Exception:
        pass
    return None

# ---------- clips ----------
for spec in cfg['clip_files']:
    path = spec['file']
    stem = os.path.splitext(os.path.basename(path))[0].lstrip('@')
    objs_before = set(o.name for o in bpy.data.objects)
    acts_before = set(a.name for a in bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path, use_anim=True, **IMP)
    new_objs = [o for o in bpy.data.objects if o.name not in objs_before]
    new_acts = [a for a in bpy.data.actions if a.name not in acts_before]
    imp_arms = [o for o in new_objs if o.type == 'ARMATURE']
    if len(imp_arms) != 1:
        prob("%s: expected 1 imported armature, got %r" % (stem, [o.name for o in imp_arms]))
        continue
    ia = imp_arms[0]
    act = ia.animation_data.action if ia.animation_data else None
    if act is None:
        prob("%s: no action on imported armature" % stem)
        continue
    ia.animation_data.action = None
    ibones = set(b.name for b in ia.data.bones)
    only_clip = sorted(ibones - BONES)
    if only_clip:
        prob("%s: clip skeleton has bones not in model armature: %s" % (stem, only_clip))

    missing = set()
    objpaths = set()
    for fc in act.fcurves:
        m = re.match(r'pose\.bones\["([^"]+)"\]', fc.data_path)
        if m:
            if m.group(1) not in BONES:
                missing.add(m.group(1))
        else:
            objpaths.add(fc.data_path)
    if missing:
        prob("%s: fcurves target bones absent from model armature: %s" % (stem, sorted(missing)))

    keys = [k.co[0] for fc in act.fcurves for k in fc.keyframe_points]
    kmin, kmax = (min(keys), max(keys)) if keys else (0.0, 0.0)
    report.setdefault('fps_per_file', {})[stem] = sc.render.fps
    if sc.render.fps != report.get('fps0', sc.render.fps):
        prob("%s: scene fps changed to %d (was %d) - frame/second mapping inconsistent"
             % (stem, sc.render.fps, report['fps0']))
    report['fps0'] = sc.render.fps

    for c in spec['clips']:
        name = c['name']
        f0 = c['f0'] + cfg.get('frame_offset', 1)
        f1 = c['f1'] + cfg.get('frame_offset', 1)
        cf0, cf1 = max(f0, kmin), min(f1, kmax)
        if (cf0, cf1) != (f0, f1):
            prob("%s/%s: unity range %d-%d clamped to blender key range %g-%g" % (stem, name, f0, f1, cf0, cf1))
        if cf1 <= cf0:
            prob("%s/%s: empty range, skipped" % (stem, name))
            continue
        # per-clip copy of the action, shifted so the clip starts at frame 0
        # (the glTF exporter maps frame -> time as frame/fps with no offset)
        cact = act.copy()
        cact.name = name
        cact.use_fake_user = True
        for fc in cact.fcurves:
            for k in fc.keyframe_points:
                k.co[0] -= cf0
                k.handle_left[0] -= cf0
                k.handle_right[0] -= cf0
            fc.update()
        L = cf1 - cf0
        tr = AD.nla_tracks.new()
        tr.name = name
        st = tr.strips.new(name, 0, cact)
        st.name = name
        sl = obj_slot(cact)
        if sl is not None:
            try:
                st.action_slot = sl
            except Exception as e:
                prob("%s/%s: slot bind failed %s" % (stem, name, e))
        try:
            st.use_sync_length = False
            st.action_frame_start = 0.0
            st.action_frame_end = L
            st.frame_start_ui = 0.0
            st.frame_end_ui = L
            st.scale = 1.0
            st.repeat = 1.0
            st.frame_end_ui = L
        except Exception as e:
            prob("%s/%s: strip trim failed %s" % (stem, name, e))
        if abs(st.scale - 1.0) > 1e-4 or abs(st.frame_start) > 1e-4 or abs(st.frame_end - L) > 1e-3:
            prob("%s/%s: strip geometry off: start=%g end=%g scale=%g (want 0..%g scale 1)"
                 % (stem, name, st.frame_start, st.frame_end, st.scale, L))
        st.extrapolation = 'NOTHING'
        st.blend_type = 'REPLACE'
        st.influence = 1.0
        got = [round(st.action_frame_start, 2), round(st.action_frame_end, 2),
               round(st.frame_start, 2), round(st.frame_end, 2)]
        info = {'name': name, 'src': os.path.basename(path), 'unity': [c['f0'], c['f1']],
                'blender_strip': got, 'frames': int(round(cf1-cf0))+1,
                'dur_s': round((cf1-cf0)/sc.render.fps, 3),
                'nfcurves': len(act.fcurves), 'obj_channels': sorted(objpaths),
                'missing_bones': sorted(missing)}
        report['clips'].append(info)
        print("CLIP", name, info['unity'], "->", got, "dur", info['dur_s'])

    for o in new_objs:
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except Exception as e:
            prob("delete %s: %s" % (o.name, e))
    for a in new_acts:
        if a is not act and a.users == 0:
            try:
                bpy.data.actions.remove(a)
            except Exception:
                pass

for _ in range(3):
    try:
        bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
    except Exception as e:
        print("purge skipped", e)

report['nla_tracks'] = [t.name for t in AD.nla_tracks]
print("TRACKS", len(AD.nla_tracks))

# ---------- export ----------
for o in bpy.context.scene.objects:
    try:
        o.select_set(False)
    except Exception:
        pass
for n in model_objs:
    o = bpy.data.objects.get(n)
    if o is None:
        continue
    o.hide_set(False)
    o.hide_viewport = False
    o.select_set(True)
bpy.context.view_layer.objects.active = ARM

kwargs = dict(
    filepath=OUT, export_format='GLB', use_selection=True,
    export_apply=False, export_animations=True,
    export_animation_mode='NLA_TRACKS',
    export_materials='EXPORT', export_image_format='NONE',
    export_unused_images=False, export_unused_textures=False,
    export_yup=True, export_skins=True, export_def_bones=False,
    export_lights=False, export_cameras=False, export_extras=False,
    export_optimize_animation_size=False, export_frame_range=False,
    export_rest_position_armature=True, export_anim_single_armature=True,
)
print("EXPORT", {k: v for k, v in kwargs.items() if k != 'filepath'})
res = bpy.ops.export_scene.gltf(**kwargs)
print("RESULT", res, os.path.getsize(OUT) if os.path.exists(OUT) else "MISSING")
report['glb_bytes'] = os.path.getsize(OUT) if os.path.exists(OUT) else 0
json.dump(report, open(os.path.splitext(OUT)[0] + '.blender.json', 'w'), indent=1)
print("DONE", OUT)

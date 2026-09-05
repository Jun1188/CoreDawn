import bpy, sys, json, os

argv = sys.argv[sys.argv.index('--')+1:]
cfg = json.loads(argv[0])

roots = cfg['roots']
out = cfg['out']
apply_mods = cfg.get('apply', True)

def descendants(o, acc):
    acc.add(o.name)
    for c in o.children:
        descendants(c, acc)

keep = set()
for r in roots:
    o = bpy.data.objects.get(r)
    if o is None:
        raise SystemExit("ROOT NOT FOUND: %s" % r)
    descendants(o, keep)
for ex in cfg.get('exclude', []):
    o = bpy.data.objects.get(ex)
    if o:
        sub = set(); descendants(o, sub)
        keep -= sub
        print("EXCLUDE:", sorted(sub))
print("KEEP:", sorted(keep))

# delete every other object (cameras, lights, empties, helper curves/meshes)
for o in list(bpy.data.objects):
    if o.name not in keep:
        print("DELETE:", o.name, o.type)
        bpy.data.objects.remove(o, do_unlink=True)

# --- animation wiring ---
for name in cfg.get('clear_obj_anim', []):
    o = bpy.data.objects.get(name)
    if o and o.animation_data:
        o.animation_data_clear()
        print("CLEARED obj anim:", name)

for name, actname in cfg.get('sk_action', {}).items():
    o = bpy.data.objects[name]
    sk = o.data.shape_keys
    act = bpy.data.actions[actname]
    if sk.animation_data is None:
        sk.animation_data_create()
    sk.animation_data.action = act
    # bind the KEY slot explicitly (Blender 4.4 slotted actions)
    for s in act.slots:
        if s.target_id_type == 'KEY':
            sk.animation_data.action_slot = s
            break
    print("SK action:", name, "->", actname, "slot:",
          sk.animation_data.action_slot.identifier if sk.animation_data.action_slot else None)

for name, spec in cfg.get('nla', {}).items():
    o = bpy.data.objects[name]
    if o.animation_data is None:
        o.animation_data_create()
    ad = o.animation_data
    # wipe existing tracks so only what we ask for is exported
    for t in list(ad.nla_tracks):
        ad.nla_tracks.remove(t)
    ad.action = None
    for actname in spec:
        act = bpy.data.actions[actname]
        tr = ad.nla_tracks.new()
        tr.name = actname
        st = tr.strips.new(actname, int(act.frame_range[0]), act)
        st.name = actname
        try:
            for s in act.slots:
                if s.target_id_type == 'OBJECT':
                    st.action_slot = s
                    break
        except Exception as e:
            print("slot bind failed:", e)
        print("NLA:", name, "track", tr.name, "strip", st.name, "action", st.action.name,
              "slot", getattr(getattr(st, 'action_slot', None), 'identifier', None))

# --- selection ---
try:
    if bpy.context.object is not None and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
except Exception as e:
    print("mode_set skipped:", e)
for o in bpy.context.scene.objects:
    try:
        o.select_set(False)
    except Exception as e:
        print("deselect failed:", o.name, e)
for n in keep:
    o = bpy.data.objects[n]
    o.hide_set(False)
    o.hide_viewport = False
    o.select_set(True)
bpy.context.view_layer.objects.active = bpy.data.objects[roots[0]]

kwargs = dict(
    filepath=out,
    export_format='GLB',
    use_selection=True,
    export_apply=apply_mods,
    export_animations=True,
    export_animation_mode=cfg.get('anim_mode', 'ACTIONS'),
    export_morph=True,
    export_morph_animation=True,
    export_morph_normal=cfg.get('morph_normal', False),   # morph mesh(belt) needs normal deltas: without them the moving strip keeps rest-pose normals (lit wrong at the rollers)
    export_morph_tangent=False,
    export_materials='EXPORT',        # keep material names ...
    export_image_format='NONE',       # ... but emit no images/textures
    export_unused_images=False,
    export_unused_textures=False,
    export_yup=True,
    export_skins=True,
    export_def_bones=False,
    export_lights=False,
    export_cameras=False,
    export_extras=False,
    export_optimize_animation_size=False,
    export_frame_range=False,
    export_rest_position_armature=True,
    export_bake_animation=False,
    export_anim_single_armature=True,
)
print("EXPORT KWARGS:", {k: v for k, v in kwargs.items() if k != 'filepath'})
res = bpy.ops.export_scene.gltf(**kwargs)
print("EXPORT RESULT:", res, "->", out, os.path.getsize(out) if os.path.exists(out) else "MISSING")

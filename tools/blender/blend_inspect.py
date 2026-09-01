import bpy, sys
print("=== FILE", bpy.data.filepath)
for o in bpy.data.objects:
    sk = o.data.shape_keys if getattr(o, 'data', None) is not None and hasattr(o.data, 'shape_keys') else None
    keys = [k.name for k in sk.key_blocks] if sk else []
    mats = [m.name if m else None for m in getattr(o.data, 'materials', [])] if o.type == 'MESH' else []
    anim = o.animation_data.action.name if o.animation_data and o.animation_data.action else None
    dims = tuple(round(x, 3) for x in o.dimensions)
    print(f"OBJ {o.name} type={o.type} parent={o.parent.name if o.parent else None} loc={tuple(round(x,3) for x in o.location)} scale={tuple(round(x,3) for x in o.scale)} dims={dims} mats={mats} shapekeys={keys} action={anim} mods={[m.type for m in o.modifiers]}")
print("ACTIONS", [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
print("SCENE frames", bpy.context.scene.frame_start, bpy.context.scene.frame_end, "unit scale", bpy.context.scene.unit_settings.scale_length)

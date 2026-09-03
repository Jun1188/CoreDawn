# CoreDawn .blend -> .glb export report

Blender 4.4 (`C:/Program Files/Blender Foundation/Blender 4.4/blender.exe`), headless.
Sources: `C:/Users/niced/Documents/Projects/GitHub/Univ/Blender/CoreDawn/*.blend` (read-only, nothing written back).
Outputs: this directory. No Unity project file was touched.

## Export settings (all files)

```
export_format='GLB'            use_selection=True          export_yup=True
export_apply=True              (False for core.glb, so the ARMATURE modifier is kept)
export_animations=True         export_animation_mode='ACTIONS'   export_optimize_animation_size=False
export_morph=True              export_morph_animation=True  export_morph_normal=False  export_morph_tangent=False
export_skins=True              export_def_bones=False       export_rest_position_armature=True
export_materials='EXPORT'      export_image_format='NONE'   export_unused_images=False  export_unused_textures=False
export_lights=False            export_cameras=False         export_extras=False
```

**Deviation from the brief (intentional):** `export_materials='PLACEHOLDER'` was *not* used.
In Blender 4.4 PLACEHOLDER keeps the per-primitive slot split but writes **no `materials` array at all**,
so the material *names* would be lost. `export_materials='EXPORT'` + `export_image_format='NONE'`
(+ `export_unused_images/textures=False`) is the equivalent that satisfies both requirements:
material names survive, and every file has `images: 0, textures: 0, samplers: 0` (verified below).

Helper objects were **deleted before export** in each headless session (not just deselected), so nothing
can leak in: `Camera`, `Empty` (camera rig, `EmptyAction`), `ShotCam`, `TempSun`, `Cam_Aim`,
and in Conveyor.blend the construction helpers `BeltPart` (Array+Curve modifiers) and `BézierCircle`.
No transforms were applied, nothing was scaled, no object hierarchy was changed.

## Per-file source selection and animation wiring

| output | source .blend | exported root + children | animation handling |
|---|---|---|---|
| miner.glb | Miner.blend | Body > Base, Drill, Head, Light, Port | Drill had two NLA tracks, no active action -> both `DrillAction` and `DrillAction.001` exported |
| smelter.glb | Smelter.blend | Body > Base, Head, Light, Melter, Molten, Port, Port.001, Pot | none (only the camera-rig `EmptyAction` existed; excluded) |
| constructor.glb | Constructor.blend | Body > Base, Head, Light, Plate, Port, Port.001, Razer | none |
| splitter.glb | Splitter.blend | Body > Base, Head, Light, Port, Port.001..003 | none |
| merger.glb | Merger.blend | Body > Base, Head, Light, Port, Port.001..003 | none |
| conveyor_port.glb | Conveyor_Port.blend | Body > Base, Head, Light, Plate, Port, Port.001, Razer | none |
| belt.glb | Conveyor.blend | Conveyor > Belt, Light | shape-key action `Belt_Action` (41 targets) |
| belt_curve_l.glb | Conveyor.blend | Conveyor.L > Belt.L, Light.L | shape-key action `Belt.L_Action` (148 targets) |
| belt_curve_r.glb | Conveyor.blend | Conveyor.R > Belt.R, Light.R | shape-key action `Belt.R_Action` (148 targets) |
| core.glb | SpaceShip.blend | SpaceShip_Rig (armature) + 11 skinned meshes | `SpaceShip_Landing` + `SpaceShip_Takeoff` |

### Source-data notes that required a decision

1. **Blender 4.4 slotted actions.** Every belt action carries two slots (a `KEY` slot that actually holds the
   148/41 `key_blocks[...].value` f-curves, and an empty `OBJECT` slot). The mesh objects `Belt`, `Belt.L`,
   `Belt.R` also had these actions bound at *object* level, which animates nothing. Those object-level bindings
   were cleared before export so each glb contains exactly one animation, the morph one.
2. **`Belt.R` was bound to the wrong action.** `Belt.R`'s shape-key animation data pointed at
   `Belt.L_Action.002`, while `Belt.R_Action` sat on the (dead) object slot. As instructed, `Belt.R_Action`
   was bound to the `KEY` slot before export. Keyframe-for-keyframe comparison shows
   `Belt.R_Action == Belt.L_Action.002 == Belt.L_Action` (identical 148 curves), so this is a naming fix only -
   the L/R mirroring lives in the morph target geometry, not in the weight curves.
3. **`Hull` carried the armature action.** In SpaceShip.blend the mesh object `Hull` had `SpaceShip_Landing`
   assigned with no slot bound (a stray binding; the action only has an `OBSpaceShip_Rig` slot). It was cleared,
   and both actions were placed on NLA tracks of `SpaceShip_Rig` so `ACTIONS` mode emits exactly two animations.
4. **`Constructor.blend` and `Conveyor_Port.blend` are the same file** (identical md5 `c890200c...`).
   `constructor.glb` and `conveyor_port.glb` are therefore byte-identical. If they are meant to be different
   models, the source needs fixing - the export cannot invent a difference.
5. **Armature scale 0.21 had to be restored by hand (see core.glb).** The Blender glTF exporter normalizes the
   armature object transform: it wrote `SpaceShip_Rig` with *no* transform and exported the object-level
   animation relative to that, so the ship came out 1/0.21 = 4.76x too large (13.32 x 4.59 x 7.00).
   Additionally the source `SpaceShip_Landing` action keys the rig's object scale at a constant **1.0** even
   though the object scale is 0.21 - i.e. playing Landing in Blender itself blows the ship up 4.76x.
   Post-process (`patch_core.py`, JSON chunk only, BIN chunk copied byte for byte):
   * `nodes["SpaceShip_Rig"].scale = [0.21, 0.21, 0.21]`
   * removed the one constant-1.0 `scale` channel targeting `SpaceShip_Rig` in `SpaceShip_Landing`
     (54 -> 53 channels; it carried no information, and left in place it would have cancelled the 0.21).
   Bone channels, translations and rotations are untouched. Result: **2.7967 x 0.9648 x 1.4698**, matching the
   ~2.8 x 0.97 x 1.47 of the current fbx.

## Verification (GLB JSON chunk parsed directly, struct-unpacked)

`raw POSITION accessor bounds` = union of all POSITION accessor min/max (mesh-local).
`world-space rest bounds` = the same corners pushed through the node hierarchy (and, for skinned meshes,
through `joint_global * inverseBindMatrix`), i.e. what Unity should show at rest, Y-up.

## miner.glb

- size 264,580 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 6 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Body [mesh=Cube.001]
    - Base [mesh=Plane.004]
    - Drill [mesh=Cone.001]  T=[0, 0.23, 0] R=[0, -1, 0, 0.0]
    - Head [mesh=Cube.008]
    - Light [mesh=Cube.009]
    - Port [mesh=Cube.007]

- meshes: 6, primitives: 6
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- animations: 2
  - 'DrillAction': channels=1 samplers=1 time=[0.042 .. 0.167]s
      rotation    1 channel(s) -> ['Drill']
  - 'DrillAction.001': channels=2 samplers=2 time=[0.042 .. 10.000]s
      rotation    1 channel(s) -> ['Drill']
      translation 1 channel(s) -> ['Drill']
- POSITION accessor bounds combined (mesh-local): min=[-0.4015, -0.2, -0.4015] max=[0.5052, 0.8837, 0.4015] size=[0.9067, 1.0837, 0.8031]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.4015, -0.0, -0.4015] max=[0.5052, 0.8837, 0.4015] SIZE (x, y-height, z) = [0.9067, 0.8837, 0.8031]

## smelter.glb

- size 313,372 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 9 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Body [mesh=Cube.001]
    - Base [mesh=Plane.004]
    - Head [mesh=Cube.008]
    - Light [mesh=Cube.009]
    - Melter [mesh=Plane.001]
    - Molten [mesh=Cylinder.001]  T=[0, -0.381, 0] R=[0, 0.1951, 0, 0.9808] S=[0.629, 0.3965, 0.629]
    - Port [mesh=Cube.007]
    - Port.001 [mesh=Cube.002]  R=[1, 0, 0, 0] S=[-1, -1, -1]
    - Pot [mesh=Cylinder]  T=[0, -0.381, 0] R=[0, 0.1951, 0, 0.9808] S=[0.629, 0.3965, 0.629]

- meshes: 9, primitives: 9
- materials (2, in glTF order): ['FactoryColor', 'Molten']
- images: 0, textures: 0, samplers: 0
- animations: 0
- POSITION accessor bounds combined (mesh-local): min=[-0.5449, -0.0, -0.495] max=[0.5418, 1.495, 0.584] size=[1.0867, 1.495, 1.079]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5052, -0.0, -0.4181] max=[0.5052, 0.903, 0.4705] SIZE (x, y-height, z) = [1.0104, 0.903, 0.8886]

## constructor.glb

- size 330,168 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 8 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Body [mesh=Cube.001]
    - Base [mesh=Plane.004]
    - Head [mesh=Cube.008]
    - Light [mesh=Cube.009]
    - Plate [mesh=Cube]
    - Port [mesh=Cube.007]
    - Port.001 [mesh=Cube.002]  R=[1, 0, 0, 0] S=[-1, -1, -1]
    - Razer [mesh=Cylinder]  S=[0.1377, 0.1377, 0.1377]

- meshes: 8, primitives: 8
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- animations: 0
- POSITION accessor bounds combined (mesh-local): min=[-0.8881, -0.0, -0.8881] max=[0.8881, 5.3518, 0.8881] size=[1.7762, 5.3518, 1.7762]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5052, -0.0, -0.4015] max=[0.5052, 0.8679, 0.4015] SIZE (x, y-height, z) = [1.0104, 0.8679, 0.8031]

## splitter.glb

- size 292,264 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 8 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Body [mesh=Cube.001]
    - Base [mesh=Plane.004]
    - Head [mesh=Cube.008]
    - Light [mesh=Cube.009]
    - Port [mesh=Cube.007]
    - Port.001 [mesh=Cube.002]
    - Port.002 [mesh=Cube.003]
    - Port.003 [mesh=Cube.004]

- meshes: 8, primitives: 8
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- animations: 0
- POSITION accessor bounds combined (mesh-local): min=[-0.5052, -0.0, -0.5052] max=[0.5052, 0.5353, 0.5052] size=[1.0104, 0.5353, 1.0104]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5052, -0.0, -0.5052] max=[0.5052, 0.5353, 0.5052] SIZE (x, y-height, z) = [1.0104, 0.5353, 1.0104]

## merger.glb

- size 292,268 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 8 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Body [mesh=Cube.001]
    - Base [mesh=Plane.004]
    - Head [mesh=Cube.008]
    - Light [mesh=Cube.009]
    - Port [mesh=Cube.007]
    - Port.001 [mesh=Cube.002]
    - Port.002 [mesh=Cube.003]
    - Port.003 [mesh=Cube.004]

- meshes: 8, primitives: 8
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- animations: 0
- POSITION accessor bounds combined (mesh-local): min=[-0.5052, -0.0, -0.5052] max=[0.5052, 0.5353, 0.5052] size=[1.0104, 0.5353, 1.0104]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5052, -0.0, -0.5052] max=[0.5052, 0.5353, 0.5052] SIZE (x, y-height, z) = [1.0104, 0.5353, 1.0104]

## conveyor_port.glb

- size 330,168 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 8 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Body [mesh=Cube.001]
    - Base [mesh=Plane.004]
    - Head [mesh=Cube.008]
    - Light [mesh=Cube.009]
    - Plate [mesh=Cube]
    - Port [mesh=Cube.007]
    - Port.001 [mesh=Cube.002]  R=[1, 0, 0, 0] S=[-1, -1, -1]
    - Razer [mesh=Cylinder]  S=[0.1377, 0.1377, 0.1377]

- meshes: 8, primitives: 8
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- animations: 0
- POSITION accessor bounds combined (mesh-local): min=[-0.8881, -0.0, -0.8881] max=[0.8881, 5.3518, 0.8881] size=[1.7762, 5.3518, 1.7762]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5052, -0.0, -0.4015] max=[0.5052, 0.8679, 0.4015] SIZE (x, y-height, z) = [1.0104, 0.8679, 0.8031]

## belt.glb

- size 1,969,296 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 3 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Conveyor [mesh=Plane.004]
    - Belt [mesh=Belt_baked]
    - Light [mesh=Cube.009]

- meshes: 3, primitives: 3
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- morph targets: mesh 'Belt_baked' prim 0 -> 41 targets (names: ['belt_000', 'belt_001', 'belt_002'] ... ['belt_040'])
  mesh 'Belt_baked' default weights array length: 41
- animations: 1
  - 'Belt_Action': channels=1 samplers=1 time=[0.000 .. 1.708]s
      weights     1 channel(s) -> ['Belt']
      weights channel on node 'Belt': 42 keyframes x 41 weights, interp=LINEAR
- POSITION accessor bounds combined (mesh-local): min=[-0.5, -0.0201, -0.1971] max=[0.5, 0.153, 0.1971] size=[1.0, 0.1732, 0.3942]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5, -0.0201, -0.1971] max=[0.5, 0.153, 0.1971] SIZE (x, y-height, z) = [1.0, 0.1732, 0.3942]

## belt_curve_l.glb

- size 8,352,136 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 3 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Conveyor.L [mesh=Plane.003]
    - Belt.L [mesh=Belt.L]
    - Light.L [mesh=Cube.001]

- meshes: 3, primitives: 3
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- morph targets: mesh 'Belt.L' prim 0 -> 148 targets (names: ['belt_000', 'belt_001', 'belt_002'] ... ['belt_147'])
  mesh 'Belt.L' default weights array length: 148
- animations: 1
  - 'Belt.L_Action': channels=1 samplers=1 time=[0.000 .. 6.208]s
      weights     1 channel(s) -> ['Belt.L']
      weights channel on node 'Belt.L': 150 keyframes x 148 weights, interp=LINEAR
- POSITION accessor bounds combined (mesh-local): min=[-0.5, -0.0201, -0.5] max=[0.1971, 0.153, 0.1971] size=[0.6971, 0.1732, 0.6971]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5, -0.0201, -0.5] max=[0.1971, 0.153, 0.1971] SIZE (x, y-height, z) = [0.6971, 0.1732, 0.6971]

## belt_curve_r.glb

- size 8,352,140 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 3 (0 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - Conveyor.R [mesh=Plane.001]
    - Belt.R [mesh=BeltPart.R]
    - Light.R [mesh=Cube.002]

- meshes: 3, primitives: 3
- materials (1, in glTF order): ['FactoryColor']
- images: 0, textures: 0, samplers: 0
- morph targets: mesh 'BeltPart.R' prim 0 -> 148 targets (names: ['belt_000', 'belt_001', 'belt_002'] ... ['belt_147'])
  mesh 'BeltPart.R' default weights array length: 148
- animations: 1
  - 'Belt.R_Action': channels=1 samplers=1 time=[0.000 .. 6.208]s
      weights     1 channel(s) -> ['Belt.R']
      weights channel on node 'Belt.R': 150 keyframes x 148 weights, interp=LINEAR
- POSITION accessor bounds combined (mesh-local): min=[-0.5, -0.0201, -0.1971] max=[0.1971, 0.153, 0.5] size=[0.6971, 0.1732, 0.6971]
- world-space rest bounds (Y-up, hierarchy applied): min=[-0.5, -0.0201, -0.1971] max=[0.1971, 0.153, 0.5] SIZE (x, y-height, z) = [0.6971, 0.1732, 0.6971]

## core.glb

- size 4,540,664 bytes | glTF 2 | generator: Khronos glTF Blender I/O v4.4.56
- nodes: 29 (17 of them are joints/bones)
- node hierarchy (bone subtrees collapsed):
  - SpaceShip_Rig  S=[0.21, 0.21, 0.21]
    - Doors_Antenna [mesh=Doors_Antenna, skin=0]
    - Hover_Housing [mesh=Hover_Housing, skin=0]
    - Hull [mesh=Hull, skin=0]
    - Interior_Wall [mesh=Interior_Wall, skin=0]
    - Landing_Legs [mesh=Landing_Legs, skin=0]
    - Light_Strip [mesh=Light_Strip, skin=0]
    - Pipe_Frame [mesh=Pipe_Frame, skin=0]
    - Seats [mesh=Seats, skin=0]
    - Thruster [mesh=Thruster, skin=0]
    - Window_Frame_Front [mesh=Window_Frame_Front, skin=0]
    - Windows [mesh=Windows, skin=0]
    - Root  T=[-0.0089, 0.54, -0.0] R=[0.0, -0.0, 0.7071, 0.7071]  (bone root; subtree of 17 bones collapsed)

- meshes: 11, primitives: 11
- materials (2, in glTF order): ['ShipColor', 'ShipGlass']
- images: 0, textures: 0, samplers: 0
- skin 0: joints=17, first joint node=Root
- animations: 2
  - 'SpaceShip_Landing': channels=53 samplers=53 time=[0.042 .. 8.750]s
      rotation    18 channel(s) -> ['Door_Ctrl_L', 'Door_Ctrl_R', 'Door_Lower_L', 'Door_Lower_R', 'Door_Upper_L', 'Door_Upper_R', '...+12 more']
      scale       17 channel(s) -> ['Door_Ctrl_L', 'Door_Ctrl_R', 'Door_Lower_L', 'Door_Lower_R', 'Door_Upper_L', 'Door_Upper_R', '...+11 more']
      translation 18 channel(s) -> ['Door_Ctrl_L', 'Door_Ctrl_R', 'Door_Lower_L', 'Door_Lower_R', 'Door_Upper_L', 'Door_Upper_R', '...+12 more']
  - 'SpaceShip_Takeoff': channels=53 samplers=53 time=[0.042 .. 10.000]s
      rotation    18 channel(s) -> ['Door_Ctrl_L', 'Door_Ctrl_R', 'Door_Lower_L', 'Door_Lower_R', 'Door_Upper_L', 'Door_Upper_R', '...+12 more']
      scale       17 channel(s) -> ['Door_Ctrl_L', 'Door_Ctrl_R', 'Door_Lower_L', 'Door_Lower_R', 'Door_Upper_L', 'Door_Upper_R', '...+11 more']
      translation 18 channel(s) -> ['Door_Ctrl_L', 'Door_Ctrl_R', 'Door_Lower_L', 'Door_Lower_R', 'Door_Upper_L', 'Door_Upper_R', '...+12 more']
- POSITION accessor bounds combined (mesh-local): min=[-6.34, -0.1906, -3.5379] max=[6.9774, 4.4038, 3.4612] size=[13.3174, 4.5943, 6.9991]
- skin rest check: joint_global * inverseBindMatrix is identical for all 17 joints at rest: True (matrix diag=[0.21, 0.21, 0.21])
- world-space rest bounds (Y-up, hierarchy applied): min=[-1.3314, -0.04, -0.743] max=[1.4652, 0.9248, 0.7269] SIZE (x, y-height, z) = [2.7967, 0.9648, 1.4698]

## Round-trip check

Every glb was re-imported into a clean Blender scene: node names, parenting, shape-key counts, action names,
material names and world bounds all come back as exported. (The importer adds an `Icosphere` in a
`glTF_not_exported` collection when importing an armature - that is the importer's bone-display placeholder,
it is not in the file: `core.glb` has 29 nodes / 11 meshes and no Icosphere.)

Nothing in the brief failed. The only two places where the literal instruction was not followed are documented
above: `export_materials='PLACEHOLDER'` (would have dropped material names) and the manual restoration of the
armature scale in `core.glb` (the exporter refuses to write it).

## Addendum 2026-09-04 — belt morph normals

`export_morph_normal=False` left the belt morph targets with `POSITION` deltas only. The whole strip cycles
(every vertex moves), so the segment wrapping around the roller kept its rest-pose normal and was lit as if
facing the sky (light blue). `export_glb.py` now takes cfg `morph_normal` (default False, kept for the
other files); `belt.glb`, `belt_curve_l.glb`, `belt_curve_r.glb` were re-exported from `Conveyor.blend` with
`{"roots":["Conveyor"],"sk_action":{"Belt":"Belt_Action"},"clear_obj_anim":["Belt"],"morph_normal":true}` (and
the `.L`/`.R` equivalents). Structural diff against the previous files: identical nodes, bounds, animations and
materials; targets gained `NORMAL`. Sizes: belt 1,969,380 → 3,228,164 B, each curve 8,352,2xx → 14,979,68x B.

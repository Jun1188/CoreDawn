import os, re, json, glob

CP = r"C:/Users/niced/Documents/Projects/GitHub/Univ/TeamProj2606/Assets/ThirdParty/3D Game Kit - Character Pack/Characters"
OUTDIR = r"C:/Users/niced/AppData/Local/Temp/claude/c--Users-niced-Documents-Projects-GitHub-Univ-TeamProj2606/03ad60a7-d9c9-4f59-aeef-e23f52f371c5/scratchpad/blender_out_monsters"
SP = os.path.dirname(OUTDIR)
os.makedirs(OUTDIR, exist_ok=True)


def clips_of(fbx):
    txt = open(fbx + '.meta', encoding='utf-8', errors='ignore').read()
    ca = txt.split('clipAnimations:')[1] if 'clipAnimations:' in txt else ''
    names = [m.strip() for m in re.findall(r'^\s+name:\s*(.+)$', ca, re.M)]
    f0 = [int(x) for x in re.findall(r'^\s+firstFrame:\s*(-?\d+)', ca, re.M)]
    f1 = [int(x) for x in re.findall(r'^\s+lastFrame:\s*(-?\d+)', ca, re.M)]
    loop = [int(x) for x in re.findall(r'^\s+loopTime:\s*(\d+)', ca, re.M)]
    n = min(len(names), len(f0), len(f1))
    if not n:
        stem = os.path.splitext(os.path.basename(fbx))[0].lstrip('@')
        return [{'name': stem, 'f0': 1, 'f1': 100000, 'loop': 0}]
    return [{'name': names[i], 'f0': f0[i], 'f1': f1[i],
             'loop': loop[i] if i < len(loop) else 0} for i in range(n)]


def folder(mon):
    return sorted(glob.glob(os.path.join(CP, mon, 'AnimationClips', '*.fbx')))


JOBS = {
    'basic':   dict(model=os.path.join(CP, 'Chomper/Models/Chomper.FBX'),
                    clip_dirs=['Chomper']),
    'spitter': dict(model=os.path.join(CP, 'Spitter/Models/Spitter.fbx'),
                    clip_dirs=['Chomper', 'Spitter']),
    'boss':    dict(model=os.path.join(CP, 'Grenadier/Models/Grenadier.fbx'),
                    clip_dirs=['Grenadier']),
}

index = {}
for out, j in JOBS.items():
    files = []
    for d in j['clip_dirs']:
        files += folder(d)
    cfg = {'model': j['model'].replace('\\', '/'),
           'out': os.path.join(OUTDIR, out + '.glb').replace('\\', '/'),
           'frame_offset': 1,
           'clip_files': [{'file': f.replace('\\', '/'), 'clips': clips_of(f)} for f in files]}
    p = os.path.join(SP, 'cfg_%s.json' % out)
    json.dump(cfg, open(p, 'w'), indent=1)
    index[out] = p
    print(out, '->', len(cfg['clip_files']), 'files,',
          sum(len(c['clips']) for c in cfg['clip_files']), 'clips')
    for cf in cfg['clip_files']:
        print('   ', os.path.basename(cf['file']), [c['name'] for c in cf['clips']])
json.dump(index, open(os.path.join(SP, 'cfg_index.json'), 'w'), indent=1)

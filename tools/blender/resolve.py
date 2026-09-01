import os, re, json
SP = r"C:/Users/niced/AppData/Local/Temp/claude/c--Users-niced-Documents-Projects-GitHub-Univ-TeamProj2606/03ad60a7-d9c9-4f59-aeef-e23f52f371c5/scratchpad"
g2p = json.load(open(SP + "/guidmap.json"))
ART = r"C:/Users/niced/Documents/Projects/GitHub/Univ/TeamProj2606/Assets/Art/Animation/Monsters"

def clip_name(fileid, meta):
    txt = open(meta, encoding='utf-8', errors='ignore').read()
    # internalIDToNameTable: rows "74: <id>" -> second: <name>
    for m in re.finditer(r'-\s+first:\s*\n\s+74:\s*(-?\d+)\s*\n\s+second:\s*(.+)', txt):
        if m.group(1) == str(fileid):
            return m.group(2).strip()
    names = re.findall(r'^\s+- serializedVersion: \d+\s*\n\s+name:\s*(.+)$', txt, re.M)
    ca = txt.split('clipAnimations:')[1] if 'clipAnimations:' in txt else ''
    names = [n.strip() for n in re.findall(r'^\s+name:\s*(.+)$', ca, re.M)]
    idx = (int(fileid) - 7400000) // 2
    if 0 <= idx < len(names):
        return names[idx]
    return f"?fileID {fileid} (clips: {names})"

def resolve(fileid, guid):
    p = g2p.get(guid)
    if not p: return "UNRESOLVED guid " + guid
    n = os.path.basename(p)
    if p.lower().endswith('.fbx'):
        return f"{n} -> clip '{clip_name(fileid, p + '.meta')}'"
    return n + f"  [fileID {fileid}]"

out = {}
for mon in ['Chomper','Spitter','Grenadier']:
    txt = open(os.path.join(ART, f'Monster_{mon}.overrideController'), encoding='utf-8').read()
    print("=====", mon)
    for of, og, vf, vg in re.findall(r'm_OriginalClip:\s*\{fileID:\s*(-?\d+),\s*guid:\s*(\w+).*?\}\s*\n\s*m_OverrideClip:\s*\{fileID:\s*(-?\d+),\s*guid:\s*(\w+)', txt):
        print(f"  {os.path.basename(g2p.get(og,'?'+og)):16s} -> {resolve(vf, vg)}")

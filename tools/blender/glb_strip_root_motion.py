import struct, json, sys
def strip(src, dst, root_names):
    f=open(src,'rb').read(); ln=struct.unpack('<I',f[12:16])[0]; j=json.loads(f[20:20+ln]); rest=f[20+ln:]
    roots={i for i,n in enumerate(j['nodes']) if n.get('name') in root_names}
    removed=0
    for a in j.get('animations',[]):
        keep=[c for c in a['channels'] if c['target'].get('node') not in roots]
        removed+=len(a['channels'])-len(keep); a['channels']=keep
    js=json.dumps(j,separators=(',',':')).encode('utf-8')
    while len(js)%4: js+=b' '
    open(dst,'wb').write(b'glTF'+struct.pack('<II',2,12+8+len(js)+len(rest))+struct.pack('<I',len(js))+b'JSON'+js+rest)
    print(dst.split('/')[-1], 'root nodes', sorted(j['nodes'][i]['name'] for i in roots), 'removed channels', removed, 'anims', len(j.get('animations',[])), 'mats', [m.get('name') for m in j['materials']], 'scene roots', [j['nodes'][i].get('name') for i in j['scenes'][0]['nodes']])
src=sys.argv[1]; dst=sys.argv[2]; strip(src, dst, sys.argv[3].split(','))

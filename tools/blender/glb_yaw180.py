import struct, json, sys
def patch(path):
    f=open(path,'rb').read()
    assert f[:4]==b'glTF'
    ln=struct.unpack('<I',f[12:16])[0]; j=json.loads(f[20:20+ln]); rest=f[20+ln:]
    for i in j['scenes'][0]['nodes']:
        n=j['nodes'][i]
        assert 'rotation' not in n, (path, n.get('name'), n.get('rotation'))
        n['rotation']=[0.0,1.0,0.0,0.0]
        n['extras']=dict(n.get('extras',{}), yaw180='Blender→Unity 앞 방향 규약(fbx 임포트와 같게)')
    js=json.dumps(j,separators=(',',':')).encode('utf-8')
    while len(js)%4: js+=b' '
    out=b'glTF'+struct.pack('<II',2,12+8+len(js)+len(rest))+struct.pack('<I',len(js))+b'JSON'+js+rest
    open(path,'wb').write(out); print('patched', path.split('/')[-1], [j['nodes'][i]['name'] for i in j['scenes'][0]['nodes']])
for k in sys.argv[1:]: patch(f'Assets/StreamingAssets/packs/coredawn/models/{k}.glb')
